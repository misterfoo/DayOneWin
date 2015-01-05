namespace DayOneWin
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Threading.Tasks;
	using DropNet;
	using DropNet.Models;
	using Newtonsoft.Json;

	/// <summary>
	/// Handles communication with the Dropbox server
	/// </summary>
	class Dropbox
	{
		static Dropbox()
		{
			ApiKeyFile = Path.Combine( App.StorageFolder, "apiKeys.txt" );
			if( File.Exists( ApiKeyFile ) )
				LoadApiKeys();
		}

		public static string ApiKeyFile { get; private set; }
		public static string AppKey { get; private set; }
		public static string AppSecret { get; private set; }

		/// <summary>
		/// Creates a Dropbox connection object.
		/// </summary>
		public static Dropbox Connect( string token, string rootPath )
		{
			if( !string.IsNullOrEmpty( rootPath ) && !rootPath.EndsWith( "/" ) )
				rootPath += "/";

			Dropbox d = new Dropbox();
			d.m_rootPath = rootPath;
			d.RawClient = new DropNetClient( AppKey, AppSecret, null, DropNetClient.AuthenticationMethod.OAuth2 );
			if( token != null )
			{
				UserLogin login = new UserLogin();
				login.Token = token;
				d.RawClient.UserLogin = login;
			}
			return d;
		}

		private string m_rootPath;

		/// <summary>
		/// The raw DropNet client object, for direct calls.
		/// </summary>
		public DropNetClient RawClient { get; private set; }

		/// <summary>
		/// The types of progress messages we can generate.
		/// </summary>
		public enum ProgressType
		{
			Info,
			Complete,
			Error,
		}

		/// <summary>
		/// Callback function for reporting progress when talking to Dropbox.
		/// </summary>
		/// <param name="type">The type of message being reported.</param>
		/// <param name="message">The message content.</param>
		/// <returns>True to keep going, false to cancel.</returns>
		public delegate bool SyncProgressDelegate( ProgressType type, string message,
			int stepNum = 0, int stepCount = 0 );

		/// <summary>
		/// Reloads the journal entry list from the server, storing downloaded entries on disk.
		/// </summary>
		public Task RefreshFromServerAsync( SyncProgressDelegate progress )
		{
			return Task.Run( () => RefreshFromServer( progress ) );
		}

		/// <summary>
		/// Asynchronously pushes the latest content for the given journal entry up
		/// to the Dropbox server.
		/// </summary>
		public Task PushToServerAsync( SyncProgressDelegate progress, Guid entryId )
		{
			return Task.Run( () => PushToServer( progress, entryId ) );
		}

		private void RefreshFromServer( SyncProgressDelegate progress )
		{
			if( !progress( ProgressType.Info, "Connecting..." ) )
				return;

			MetaData md;
			try
			{
				md = this.RawClient.GetMetaData( m_rootPath );
			}
			catch( DropNet.Exceptions.DropboxException ex )
			{
				progress( ProgressType.Error, ex.Message );
				return;
			}

			// Look for any new entries we don't have already, or ones which have changed.
			if( !progress( ProgressType.Info, "Checking for changes" ) )
				return;
			var changed = new List<Guid>();
			foreach( MetaData mdCurrent in md.Contents )
			{
				Guid id = GetEntryIdFromFileName( mdCurrent.Name );
				if( id == Guid.Empty )
					continue;

				// Is this a totally new entry?
				string localFile = GetLocalEntryFilePath( id );
				string serverMdFile = GetServerMetadataPath( id );
				if( !File.Exists( localFile ) || !File.Exists( serverMdFile	) )
				{
					changed.Add( id );
					continue;
				}

				// Did it change from what we last knew about it?
				MetaData mdLastKnown = ReadServerMetadata( id );
				if( mdLastKnown.UTCDateModified != mdCurrent.UTCDateModified )
				{
					changed.Add( id );
					continue;
				}

				// We already have the latest copy of this entry.
			}

			// Download all the new/changed entries.
			int step = 0;
			foreach( Guid id in changed )
			{
				if( !progress( ProgressType.Info, "Downloading entry content", ++step, changed.Count ) )
					return;
				PullFromServer( progress, id );
			}
		}

		private void PullFromServer( SyncProgressDelegate progress, Guid entryId )
		{
			try
			{
				// Download the entry content to memory.
				var result = this.RawClient.GetFile( GetDropboxEntryFilePath( entryId ) );
				if( result.StatusCode != HttpStatusCode.OK )
				{
					ReportError( progress, "Upload failed", result );
					return;
				}

				// Find the metadata for the content we actually got.
				var mdRaw = result.Headers.FirstOrDefault( x => x.Name == "x-dropbox-metadata" );
				if( mdRaw == null )
				{
					progress( ProgressType.Error, "Got unexpected results from Dropbox API (no metadata for downloaded file)" );
					return;
				}

				// Write the journal entry to the local store.
				JournalEntry entry = JournalEntry.Load( result.RawBytes );
				entry.Save( App.OfflineEntryStore );

				// Record that the file on disk matches Dropbox and doesn't need to be uploaded.
				DateTime lastWrite = File.GetLastWriteTimeUtc( GetLocalEntryFilePath( entryId ) );
				WriteLocalSyncInfo( entryId, new LocalSyncInfo { LastUploadedVersion = lastWrite } );

				// Write the matching server metadata.
				MetaData mdLatest = JsonConvert.DeserializeObject<MetaData>( (string)mdRaw.Value );
				WriteServerMetadata( entryId, mdLatest );
			}
			catch( Exception ex )
			{
				progress( ProgressType.Error, string.Format(
					"Error loading entry {0}: {1}", entryId, ex.Message ) );
			}
		}

		private void PushToServer( SyncProgressDelegate progress, Guid entryId )
		{
			progress( ProgressType.Complete, "Uploading..." );

			MetaData mdLastKnown = ReadServerMetadata( entryId );

			byte[] rawData;
			DateTime lastWrite;
			JournalEntry.GetCleanBytes( GetLocalEntryFilePath( entryId ), out rawData, out lastWrite );

			// Upload the data to Dropbox.
			var result = this.RawClient.UploadFile(
				m_rootPath, JournalEntry.GetDataFileName( entryId ), rawData,
				overwrite: true, parentRevision: mdLastKnown.Rev );
			if( result.StatusCode != HttpStatusCode.OK )
			{
				ReportError( progress, "Upload failed", result );
				return;
			}

			// Remember that we pushed this version, and the server metadata which matches it.
			WriteLocalSyncInfo( entryId, new LocalSyncInfo { LastUploadedVersion = lastWrite } );
			WriteServerMetadata( entryId, result.Data );

			progress( ProgressType.Complete, "Upload complete" );
		}

		private static void ReportError( SyncProgressDelegate progress, string context, RestSharp.IRestResponse result )
		{
			string msg = string.Format( "{0}: {1}", context, result.StatusCode );
			progress( ProgressType.Error, msg );
		}

		/// <summary>
		/// Gets the last known server metadata for the given journal entry
		/// </summary>
		private MetaData ReadServerMetadata( Guid entryId )
		{
			string file = GetServerMetadataPath( entryId );
			if( !File.Exists( file ) )
				return null;
			return JsonConvert.DeserializeObject<MetaData>( File.ReadAllText( file ) );
		}

		/// <summary>
		/// Updates the last known server metadata for the given journal entry
		/// </summary>
		private void WriteServerMetadata( Guid entryId, MetaData metaData )
		{
			string file = GetServerMetadataPath( entryId );
			File.WriteAllText( file, JsonConvert.SerializeObject( metaData ) );
		}

		/// <summary>
		/// Gets the file which stores the last known Dropbox metadata for the given journal entry
		/// </summary>
		private string GetServerMetadataPath( Guid entryId )
		{
			return GetLocalEntryFilePath( entryId ) + ".servermd";
		}

		/// <summary>
		/// Gets the local sync info for the given journal entry
		/// </summary>
		private LocalSyncInfo ReadLocalSyncInfo( Guid entryId )
		{
			string file = GetLocalSyncInfoPath( entryId );
			if( !File.Exists( file ) )
				return null;
			return JsonConvert.DeserializeObject<LocalSyncInfo>( File.ReadAllText( file ) );
		}

		/// <summary>
		/// Updates the local sync info for the given journal entry
		/// </summary>
		private void WriteLocalSyncInfo( Guid entryId, LocalSyncInfo info )
		{
			string file = GetLocalSyncInfoPath( entryId );
			File.WriteAllText( file, JsonConvert.SerializeObject( info ) );
		}

		/// <summary>
		/// Gets the file which stores the local sync info for the given journal entry
		/// </summary>
		private string GetLocalSyncInfoPath( Guid entryId )
		{
			return GetLocalEntryFilePath( entryId ) + ".localinfo";
		}

		private string GetLocalEntryFilePath( Guid entryId )
		{
			return Path.Combine( App.OfflineEntryStore, JournalEntry.GetDataFileName( entryId ) );
		}

		private string GetDropboxEntryFilePath( Guid entryId )
		{
			return m_rootPath + JournalEntry.GetDataFileName( entryId );
		}

		private Guid GetEntryIdFromFileName( string file )
		{
			string name = Path.GetFileNameWithoutExtension( file );
			Guid id;
			if( !Guid.TryParse( name, out id ) )
				return Guid.Empty;
			return id;
		}

		private static void LoadApiKeys()
		{
			string[] lines = File.ReadAllLines( ApiKeyFile );
			if( lines.Length < 2 )
				return;
			AppKey = lines[0];
			AppSecret = lines[1];
		}

		private class LocalSyncInfo
		{
			/// <summary>
			/// The LastWriteTime of the last version we uploaded to the server
			/// </summary>
			public DateTime LastUploadedVersion { get; set; }
		}
	}
}
