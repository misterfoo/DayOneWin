namespace DayOneWin
{
	using System;
	using System.Collections.Generic;
	using System.IO;
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
			if( !rootPath.EndsWith( "/" ) )
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
		private readonly object m_fsLock = new object();

		/// <summary>
		/// The raw DropNet client object, for direct calls.
		/// </summary>
		public DropNetClient RawClient { get; private set; }

		public enum SyncResultCode
		{
			Success,
			Collision
		}

		public class SyncResult
		{
			public SyncResult( Guid entryId, SyncResultCode result )
			{
				this.EntryId = entryId;
				this.Result = result;
			}

			public Guid EntryId { get; private set; }
			public SyncResultCode Result { get; private set; }
		}

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

		public Task<SyncResult> PushToServerAsync( Guid entryId )
		{
			return Task.Run<SyncResult>( () => PushToServer( entryId ) );
		}

		private SyncResult PushToServer( Guid entryId )
		{
			MetaData mdLastKnown = GetLocalMetadata( entryId );
			MetaData mdCurrent = this.RawClient.GetMetaData( GetDropboxPath( entryId ) );
			if( mdCurrent != null && mdCurrent.ModifiedDate > mdLastKnown.ModifiedDate )
				return new SyncResult( entryId, SyncResultCode.Collision );

			byte[] rawData;
			lock( m_fsLock )
				rawData = JournalEntry.GetCleanBytes( GetLocalPath( entryId ) );

			MetaData mdNew = this.RawClient.UploadFile(
				m_rootPath, JournalEntry.GetDataFileName( entryId ), rawData,
				overwrite: true, parentRevision: mdLastKnown.Rev );

			throw new NotImplementedException();
		}

		private MetaData GetLocalMetadata( Guid entryId )
		{
			string file = GetLocalMetadataPath( entryId );
			if( !File.Exists( file ) )
				return null;
			return JsonConvert.DeserializeObject<MetaData>( File.ReadAllText( file ) );
		}

		private string GetLocalMetadataPath( Guid entryId )
		{
			return GetLocalPath( entryId ) + ".metadata";
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
	}
}
