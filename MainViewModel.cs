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
		static MainViewModel()
		{
			AutoSaveIdleTime = TimeSpan.FromSeconds( 3 );
		}

		public MainViewModel( Window view )
		{
			this.MainStatus = new StatusInfoViewModel();
			this.DropboxStatus = new StatusInfoViewModel( "Dropbox: " );

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
		/// The amount of time to wait before automatically saving the active entry; changes
		/// to the active entry will not be saved until it has been unchanged for this amount
		/// of time.
		/// </summary>
		public static TimeSpan AutoSaveIdleTime { get; set; }

		/// <summary>
		/// The main app status.
		/// </summary>
		public StatusInfoViewModel MainStatus { get; private set; }

		/// <summary>
		/// The Dropbox connection status.
		/// </summary>
		public StatusInfoViewModel DropboxStatus { get; private set; }

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

				this.DropboxStatus.Set( "Connected" );
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
					this.MainStatus.Set( result.ToString() );
					return;
				}

				DoBrowserLogout();
			}

			this.AllEntries.Clear();
			this.ActiveEntry = null;
			SetConnection( null );
		}

		/// <summary>
		/// Performs background work such as saving dirty journal entries.
		/// </summary>
		public void DoBackgroundWork()
		{
			AutoSave();
			this.MainStatus.MaybeExpire();
			this.DropboxStatus.MaybeExpire();
		}

		/// <summary>
		/// Starts an asynchronous load of the journal entryId list.
		/// </summary>
		public void RefreshEntryList()
		{
			if( m_offlineMode )
			{
				var entries = JournalEntry.LoadAllCachedEntries().ToList();
				if( entries.Count == 0 )
					entries.Add( JournalEntry.SampleEntry );
				FinishLoadEntries( entries );
			}
			else
			{
				this.MainStatus.SetSticky( "Loading entries..." );
				m_dropbox.RefreshFromServerAsync( DropboxServerProgress )
					.ContinueWith( _ => RefreshComplete() );
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

		private void RefreshComplete()
		{
			this.MainStatus.SetSticky( "Loading..." );

			var entries = JournalEntry.LoadAllCachedEntries().ToList();
			if( entries.Count == 0 )
			{
				NotifyError( "No journal entries", "Couldn't find any journal entries," +
					" because none exist or the Dropbox storage path is wrong." );
				return;
			}

			FinishLoadEntries( entries );
			this.MainStatus.Clear();
		}

		private void FinishLoadEntries( List<JournalEntry> entries )
		{
			this.AllEntries.Clear();
			foreach( JournalEntry e in entries.OrderBy( x => x.CreatedOn ) )
				this.AllEntries.Add( e );
			this.ActiveEntry = this.AllEntries.Last();
		}

		private void AutoSave()
		{
			if( !this.IsConnected || m_saving )
				return;

			try
			{
				m_saving = true;

				// Find all the entries which need saving.
				JournalEntry[] dirtyList;
				lock( this.AllEntries )
					dirtyList = this.AllEntries.Where( ShouldAutoSaveEntry ).ToArray();
				if( dirtyList.Length == 0 )
					return;

				this.MainStatus.SetSticky( "Saving..." );

				foreach( JournalEntry entry in dirtyList )
					entry.Save( App.OfflineEntryStore );
				if( !m_offlineMode )
				{
					foreach( JournalEntry entry in dirtyList )
						m_dropbox.PushToServerAsync( DropboxServerProgress, entry.Uuid );
				}

				NavInfoStringChanged();

				// A short delay so the user can see the "saving..." status.
				System.Threading.Thread.Sleep( TimeSpan.FromMilliseconds( 250 ) );
				this.MainStatus.Set( "Saved." );
			}
			finally
			{
				m_saving = false;
			}
		}
		private bool m_saving;

		private bool ShouldAutoSaveEntry( JournalEntry entry )
		{
			if( !entry.IsDirty )
				return false;

			if( entry == this.ActiveEntry )
			{
				var timeSinceEdit = DateTimeOffset.UtcNow - entry.LastChangeTime;
				return (timeSinceEdit > AutoSaveIdleTime);
			}
			else
			{
				return true;
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

		private bool DropboxServerProgress( Dropbox.ProgressType type, string message, int stepNum, int stepCount )
		{
			if( type == Dropbox.ProgressType.Error )
			{
				NotifyError( "Error talking to Dropbox", message );
				return false;
			}

			if( stepCount != 0 )
				message += string.Format( " ({0} of {1})", stepNum, stepCount );
			this.DropboxStatus.Set( message );
			return true;
		}

		/// <summary>
		/// Logs the user out of Dropbox so they will be required to enter credentials next time.
		/// </summary>
		private void DoBrowserLogout()
		{
			this.MainStatus.Set( "Logging out..." );
			var wb = new System.Windows.Controls.WebBrowser();
			wb.Navigating += ( s, a ) => Debug.WriteLine( "Navigating: " + a.Uri );
			wb.Navigated += ( s, a ) =>
				{
					this.MainStatus.Clear();
					this.DropboxStatus.Set( "Disconnected" );
					Application.Current.Dispatcher.BeginInvoke( new Action( wb.Dispose ) );
				};
			wb.Navigate( "https://www.dropbox.com/logout" );
		}

		private void NotifyError( string summary, string details )
		{
			this.MainStatus.SetSticky( summary );
			var eh = this.ErrorOccurred;
			if( eh != null )
				eh( this, summary + ": " + details );
		}

		/// <summary>
		/// A sample instance of this ViewModel for design purposes.
		/// </summary>
		public static MainViewModel Sample
		{
			get
			{
				MainViewModel mvm = new MainViewModel( null );
				mvm.ActiveEntry = JournalEntry.SampleEntry;
				mvm.MainStatus.Set( "Stuff!" );
				return mvm;
			}
		}
	}
}
