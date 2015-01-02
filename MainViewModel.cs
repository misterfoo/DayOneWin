namespace DayOneWin
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Windows;
	using System.Windows.Threading;
	using DropNet;
	using DropNet.Models;
	using Newtonsoft.Json;

	/// <summary>
	/// ViewModel for the main application window.
	/// </summary>
	class MainViewModel : BindableBase
	{
		public MainViewModel( Window view )
		{
			SetMainStatus( string.Empty );
//			this.DropboxRootPath = "/Apps/Day One/Journal.dayone/entries";
			this.DropboxRootPath = "/Scratch/JournalEntries";
			this.AllEntries = new List<JournalEntry>();

			m_offlineMode = false;
			m_view = view;
		}

		private readonly Window m_view;
		private bool m_offlineMode;

		/// <summary>
		/// The root path we should use when connecting to Dropbox.
		/// </summary>
		public string DropboxRootPath
		{
			get { return m_dropboxRoot; }
			set { SetProperty( ref m_dropboxRoot, value ); }
		}
		private string m_dropboxRoot;

		/// <summary>
		/// Indicates whether we are connected to DropBox.
		/// </summary>
		public bool IsConnected
		{
			get { return (m_dropbox != null); }
		}

		private void SetConnection( Dropbox db )
		{
			m_dropbox = db;
			OnPropertyChanged( "IsConnected" );
		}
		private Dropbox m_dropbox;

		/// <summary>
		/// Indicates whether we have an active journal entry.
		/// </summary>
		public bool HaveActiveEntry
		{
			get { return (this.ActiveEntry != null); }
		}

		/// <summary>
		/// All of the journal entries we know about.
		/// </summary>
		public List<JournalEntry> AllEntries
		{
			get; private set;
		}

		/// <summary>
		/// The active journal entry.
		/// </summary>
		public JournalEntry ActiveEntry
		{
			get { return m_active; }
			set
			{
				if( SetProperty( ref m_active, value ) )
				{
					OnPropertyChanged( "HaveActiveEntry" );
					NavInfoStringChanged();
				}
			}
		}
		private JournalEntry m_active;

		/// <summary>
		/// The index of the active journal entry (in AllEntries).
		/// </summary>
		public int ActiveEntryIndex
		{
			get
			{
				if( this.ActiveEntry == null )
					return -1;
				return this.AllEntries.IndexOf( m_active );
			}
		}

		/// <summary>
		/// The string which shows where we are in the list of entries (e.g. "2 of 200").
		/// </summary>
		public string NavigationInfoString
		{
			get
			{
				if( !this.IsConnected || m_active == null )
					return string.Empty;

				string str = string.Format( "{0} of {1}",
					this.ActiveEntryIndex + 1, this.AllEntries.Count );
				if( !m_active.EverBeenSaved )
					str += " (unsaved)";
				return str;
			}
		}

		/// <summary>
		/// Indicates whether any journal entries still have unsaved changes.
		/// </summary>
		public bool HasUnsavedChanges
		{
			get { return this.AllEntries.Any( x => x.IsDirty ); }
		}

		/// <summary>
		/// The main status bar message.
		/// </summary>
		public string MainStatusMessage
		{
			get { return m_mainStatus; }
			set { SetProperty( ref m_mainStatus, value ); }
		}
		private string m_mainStatus;

		/// <summary>
		/// The Dropbox status bar message.
		/// </summary>
		public string DropboxStatusMessage
		{
			get { return m_dropboxStatus; }
			set { SetProperty( ref m_dropboxStatus, value ); }
		}
		private string m_dropboxStatus;

		/// <summary>
		/// Raised when a substantial error occurs that the user should probably know about.
		/// </summary>
		public event EventHandler<string> ErrorOccurred;

		/// <summary>
		/// Logs the user in to Dropbox.
		/// </summary>
		public void LogIn()
		{
			if( m_offlineMode )
			{
				SetConnection( Dropbox.Connect( token: null, rootPath: null ) );
			}
			else
			{
				LoginWindow window = new LoginWindow();
				window.Owner = m_view;
				window.ShowDialog();
				if( window.UserToken == null )
				{
					NotifyError( "Login failed", "The login attempt failed or was canceled." );
					return;
				}

				SetDropboxStatus( "Connected" );
				SetConnection( Dropbox.Connect( window.UserToken, this.DropboxRootPath ) );
			}

			RefreshEntryList();
		}

		/// <summary>
		/// Logs the user out of Dropbox.
		/// </summary>
		public void LogOut()
		{
			if( !m_offlineMode && this.IsConnected )
			{
				var result = m_dropbox.RawClient.DisableAccessToken();
				if( result.StatusCode != System.Net.HttpStatusCode.OK )
				{
					SetMainStatus( result.ToString() );
					return;
				}

				DoBrowserLogout();
			}

			this.AllEntries.Clear();
			this.ActiveEntry = null;
			SetConnection( null );
		}

		/// <summary>
		/// Saves changes to any edited journal entries.
		/// </summary>
		public void AutoSave()
		{
			if( !this.IsConnected || m_saving )
				return;

			try
			{
				m_saving = true;

				// Find all the entries which need saving.
				JournalEntry[] dirtyList;
				lock( this.AllEntries )
					dirtyList = this.AllEntries.Where( x => x.IsDirty ).ToArray();
				if( dirtyList.Length == 0 )
					return;

				SetMainStatus( "Saving..." );

				if( m_offlineMode )
				{
					foreach( JournalEntry entry in dirtyList )
						entry.Save( App.OfflineEntryStore );
				}
				else
				{
					throw new NotImplementedException();
				}

				NavInfoStringChanged();

				// A short delay so the user can see the "saving..." status.
				System.Threading.Thread.Sleep( TimeSpan.FromMilliseconds( 250 ) );
				SetMainStatus( "Saved." );
			}
			finally
			{
				m_saving = false;
			}
		}
		private bool m_saving;

		/// <summary>
		/// Starts an asynchronous load of the journal entryId list.
		/// </summary>
		public void RefreshEntryList()
		{
			if( m_offlineMode )
			{
				this.AllEntries = new List<JournalEntry>();

				// Load old entries off disk.
				List<JournalEntry> entries = new List<JournalEntry>();
				foreach( string entry in Directory.EnumerateFiles( App.OfflineEntryStore,
					"*" + JournalEntry.StandardFileExtension ) )
				{
					entries.Add( JournalEntry.Load( entry ) );
				}

				if( entries.Count == 0 )
					entries.Add( JournalEntry.SampleEntry );

				FinishLoadEntries( entries );
			}
			else
			{
				SetMainStatus( "Loading entries..." );
				m_dropbox.RawClient.GetMetaDataAsync( this.DropboxRootPath,
					response => LoadEntries( response.Contents ),
					error => SetMainStatus( error.ToString() ) );
			}
		}

		/// <summary>
		/// Creates a new entry and switches to it.
		/// </summary>
		public void CreateNewEntry()
		{
			JournalEntry entry = new JournalEntry();
			entry.SetCreationInfo();
			lock( this.AllEntries )
				this.AllEntries.Add( entry );
			this.ActiveEntry = entry;
		}

		/// <summary>
		/// Navigates to the first entry in the journal.
		/// </summary>
		public void MoveToFirst()
		{
			MoveTo( 0 );
		}

		/// <summary>
		/// Navigates to the previous entry in the journal.
		/// </summary>
		public void MoveToPrevious()
		{
			MoveTo( this.ActiveEntryIndex - 1 );
		}

		/// <summary>
		/// Navigates to the next entry in the journal.
		/// </summary>
		public void MoveToNext()
		{
			MoveTo( this.ActiveEntryIndex + 1 );
		}

		/// <summary>
		/// Navigates to the last entry in the journal.
		/// </summary>
		public void MoveToLast()
		{
			MoveTo( this.AllEntries.Count - 1 );
		}

		private void LoadEntries( List<DropNet.Models.MetaData> dirContents )
		{
			if( dirContents.Count == 0 )
			{
				NotifyError( "No journal entries", "Couldn't find any journal entries," +
					" because none exist or the Dropbox storage path is wrong." );
				return;
			}

			var entries = dirContents.Where( x => !x.Is_Dir ).ToList();
			if( entries.Count == 0 )
				return;
			var latest = entries.OrderByDescending( x => x.ModifiedDate ).First();
			LoadEntry( latest );
			SetMainStatus( "Loaded!" );
		}

		private void LoadEntry( DropNet.Models.MetaData md )
		{
			m_dropbox.RawClient.GetFileAsync( md.Path,
				response => FinishLoadEntry( md, response ),
				error => NotifyError( "Error loading entry", error.Message ) );
		}

		private void FinishLoadEntries( List<JournalEntry> entries )
		{
			foreach( JournalEntry e in entries.OrderBy( x => x.CreatedOn ) )
				this.AllEntries.Add( e );
			this.ActiveEntry = this.AllEntries.Last();
		}

		private void FinishLoadEntry( MetaData lastKnownMetadata, RestSharp.IRestResponse loaded )
		{
			try
			{
				var mdRaw = loaded.Headers.FirstOrDefault( x => x.Name == "x-dropbox-metadata" );
				MetaData md = (mdRaw != null) ?
					JsonConvert.DeserializeObject<MetaData>( (string)mdRaw.Value ) :
					lastKnownMetadata;

				JournalEntry entry = JournalEntry.Load( loaded.RawBytes );
				this.ActiveEntry = entry;
			}
			catch( Exception ex )
			{
				this.ActiveEntry = null;
				NotifyError( "Error loading journal entry", ex.Message );
			}
		}

		private void MoveTo( int index )
		{
			if( index < 0 )
				index = 0;
			else if( index > this.AllEntries.Count - 1 )
				index = this.AllEntries.Count - 1;
			this.ActiveEntry = this.AllEntries[index];
		}

		private void NavInfoStringChanged()
		{
			OnPropertyChanged( "NavigationInfoString" );
		}

		/// <summary>
		/// Logs the user out of Dropbox so they will be required to enter credentials next time.
		/// </summary>
		private void DoBrowserLogout()
		{
			SetMainStatus( "Logging out..." );
			var wb = new System.Windows.Controls.WebBrowser();
			wb.Navigating += ( s, a ) => Debug.WriteLine( "Navigating: " + a.Uri );
			wb.Navigated += ( s, a ) =>
				{
					SetMainStatus();
					SetDropboxStatus( "Disconnected" );
					Application.Current.Dispatcher.BeginInvoke( new Action( wb.Dispose ) );
				};
			wb.Navigate( "https://www.dropbox.com/logout" );
		}

		private void NotifyError( string summary, string details )
		{
			SetMainStatus( summary );
			var eh = this.ErrorOccurred;
			if( eh != null )
				eh( this, summary + ": " + details );
		}

		private void SetMainStatus( string msg = null )
		{
			this.MainStatusMessage = msg ?? string.Empty;
		}

		private void SetDropboxStatus( string msg )
		{
			if( !string.IsNullOrEmpty( msg ) )
				msg = "Dropbox: " + msg;
			this.DropboxStatusMessage = msg;
		}

		/// <summary>
		/// A sample instance of this ViewModel for design purposes.
		/// </summary>
		public static MainViewModel Sample
		{
			get
			{
				return new MainViewModel( null )
				{
					ActiveEntry = JournalEntry.SampleEntry,
					MainStatusMessage = "Stuff!"
				};
			}
		}
	}
}
