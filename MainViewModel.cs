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

	/// <summary>
	/// ViewModel for the main application window.
	/// </summary>
	class MainViewModel : BindableBase
	{
		public MainViewModel( Window view )
		{
			SetStatus( "Idle" );
			this.DropboxRootPath = "/Apps/Day One/Journal.dayone/entries";
			this.AllEntries = new List<JournalEntry>();

			m_offlineMode = true;
			m_offlineStore = Path.Combine( App.StorageFolder, "entries" );
			Directory.CreateDirectory( m_offlineStore );
			m_view = view;
		}

		private readonly Window m_view;
		private bool m_offlineMode;
		private string m_offlineStore;

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

		private void SetConnection( DropNetClient client )
		{
			m_dropbox = client;
			OnPropertyChanged( "IsConnected" );
		}
		private DropNetClient m_dropbox;

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
		/// The status bar message.
		/// </summary>
		public string StatusMessage
		{
			get { return m_statusMsg; }
			set { SetProperty( ref m_statusMsg, value ); }
		}
		private string m_statusMsg;

		/// <summary>
		/// Logs the user in to Dropbox.
		/// </summary>
		public void LogIn()
		{
			if( m_offlineMode )
			{
				SetConnection( Dropbox.CreateClient() );
			}
			else
			{
				LoginWindow window = new LoginWindow();
				window.Owner = m_view;
				window.ShowDialog();
				if( window.UserToken == null )
				{
					SetStatus( "Login failed. :(" );
					return;
				}

				SetConnection( Dropbox.CreateClient( window.UserToken ) );
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
				var result = m_dropbox.DisableAccessToken();
				if( result.StatusCode != System.Net.HttpStatusCode.OK )
				{
					SetStatus( result.ToString() );
					return;
				}

				DoBrowserLogout();
			}

			this.AllEntries.Clear();
			this.ActiveEntry = null;
			SetConnection( null );
			SetStatus( "Logged out" );
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

				SetStatus( "Saving..." );

				if( m_offlineMode )
				{
					foreach( JournalEntry entry in dirtyList )
						entry.Save( m_offlineStore );
				}
				else
				{
					throw new NotImplementedException();
				}

				NavInfoStringChanged();

				// A short delay so the user can see the "saving..." status.
				System.Threading.Thread.Sleep( TimeSpan.FromMilliseconds( 250 ) );
				SetStatus( "Saved." );
			}
			finally
			{
				m_saving = false;
			}
		}
		private bool m_saving;

		/// <summary>
		/// Starts an asynchronous load of the journal entry list.
		/// </summary>
		public void RefreshEntryList()
		{
			if( m_offlineMode )
			{
				this.AllEntries = new List<JournalEntry>();

				// Load old entries off disk.
				List<JournalEntry> entries = new List<JournalEntry>();
				foreach( string entry in Directory.EnumerateFiles( m_offlineStore,
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
				SetStatus( "Loading entries..." );
				m_dropbox.GetMetaDataAsync( this.DropboxRootPath,
					response => LoadEntries( response.Contents ),
					error => SetStatus( error.ToString() ) );
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
			var entries = dirContents.Where( x => !x.Is_Dir ).ToList();
			if( entries.Count == 0 )
				return;
			var latest = entries.OrderByDescending( x => x.ModifiedDate ).First();
			LoadEntry( latest );
			SetStatus( "Loaded!" );
		}

		private void LoadEntry( DropNet.Models.MetaData md )
		{
			m_dropbox.GetFileAsync( md.Path,
				response => FinishLoadEntry( response ),
				error => SetStatus( "Error loading entry: " + error.Message ) );
		}

		private void FinishLoadEntries( List<JournalEntry> entries )
		{
			foreach( JournalEntry e in entries.OrderBy( x => x.CreatedOn ) )
				this.AllEntries.Add( e );
			this.ActiveEntry = this.AllEntries.Last();
		}

		private void FinishLoadEntry( RestSharp.IRestResponse loaded )
		{
			try
			{
				JournalEntry entry = JournalEntry.Load( loaded.RawBytes );
				this.ActiveEntry = entry;
			}
			catch( Exception ex )
			{
				this.ActiveEntry = null;
				SetStatus( ex.Message );
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
			SetStatus( "Logging out..." );
			var wb = new System.Windows.Controls.WebBrowser();
			wb.Navigating += ( s, a ) => Debug.WriteLine( "Navigating: " + a.Uri );
			wb.Navigated += ( s, a ) =>
				{
					SetStatus( "Logged out." );
					Application.Current.Dispatcher.BeginInvoke( new Action( wb.Dispose ) );
				};
			wb.Navigate( "https://www.dropbox.com/logout" );
		}

		private void SetStatus( string msg )
		{
			this.StatusMessage = msg;
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
					StatusMessage = "Stuff!"
				};
			}
		}
	}
}
