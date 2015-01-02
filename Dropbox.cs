namespace DayOneWin
{
	using System;
	using System.Collections.Generic;
	using System.IO;
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

		public List<Task<SyncResult>> SyncFromServer()
		{
			// retrieve metadata listing from server
			// determine adds/changes/deletes based on local metadata
			// foreach file
			//	download file
			//	update status information
			//	yield path to new/updated file
			throw new NotImplementedException();
		}

		/// <summary>
		/// Asynchronously pushes the latest content for the given journal entry up
		/// to the Dropbox server.
		/// </summary>
		public Task<SyncResult> PushToServerAsync( Guid entryId )
		{
			return Task.Run<SyncResult>( () => PushToServer( entryId ) );
		}

		/// <summary>
		/// The types of results we can have for a sync.
		/// </summary>
		public enum SyncResultCode
		{
			Success,
			Collision,
			Error
		}

		/// <summary>
		/// The result of a sync operation on a journal entry.
		/// </summary>
		public class SyncResult
		{
			public SyncResult( Guid entryId, SyncResultCode result )
			{
				this.EntryId = entryId;
				this.Result = result;
			}

			public SyncResult( Guid entryId, HttpStatusCode code )
			{
				this.EntryId = entryId;
				this.Result = (code == HttpStatusCode.Conflict) ?
					SyncResultCode.Collision : SyncResultCode.Error;
			}

			public Guid EntryId { get; private set; }
			public SyncResultCode Result { get; private set; }
		}

		private SyncResult PushToServer( Guid entryId )
		{
			MetaData mdLastKnown = ReadServerMetadata( entryId );

			byte[] rawData;
			DateTime lastWrite;
			JournalEntry.GetCleanBytes( GetLocalPath( entryId ), out rawData, out lastWrite );

			var result = this.RawClient.UploadFile(
				m_rootPath, JournalEntry.GetDataFileName( entryId ), rawData,
				overwrite: true, parentRevision: mdLastKnown.Rev );
			if( result.StatusCode != HttpStatusCode.OK )
				return new SyncResult( entryId, result.StatusCode );

			WriteLocalSyncInfo( entryId, new LocalSyncInfo { LastUploadedVersion = lastWrite } );
			WriteServerMetadata( entryId, result.Data );

			return new SyncResult( entryId, SyncResultCode.Success );
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
			return GetLocalPath( entryId ) + ".servermd";
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
			return GetLocalPath( entryId ) + ".localinfo";
		}

		private string GetLocalPath( Guid entryId )
		{
			return Path.Combine( App.OfflineEntryStore, JournalEntry.GetDataFileName( entryId ) );
		}

		private string GetDropboxPath( Guid entryId )
		{
			return m_rootPath + JournalEntry.GetDataFileName( entryId );
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
