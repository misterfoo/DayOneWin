namespace DayOneWin
{
	using System;
	using System.Threading;
	using System.Windows;
	using System.Windows.Controls;

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
			this.MinHeight = this.Height;
			this.MinWidth = this.Width;
			LoginButtonText = (string)this.BtnLogin.Content;

			m_model = new MainViewModel( this );
			m_model.ErrorOccurred += model_ErrorOccurred;
			this.DataContext = m_model;
			m_model.LogOut();

			TimeSpan saveInterval = TimeSpan.FromSeconds( 1 );
			m_saveTimer = new Timer( SaveTimer, null, saveInterval, saveInterval );
		}

		private static string LoginButtonText;
		private MainViewModel m_model;
		private Timer m_saveTimer;

		private void Window_Closing( object sender, System.ComponentModel.CancelEventArgs e )
		{
			if( HasUnsavedChanges() )
			{
				e.Cancel = true;
				return;
			}
		}

		private void BtnLogin_Click( object sender, RoutedEventArgs e )
		{
			// Make sure we have the necessary info to connect to Dropbox
			if( Dropbox.AppKey == null )
			{
				MessageBox.Show( this, string.Format(
						"Missing Dropbox API key info. Please create the following file and restart the app:\n\n" +
						"{0}\n\n" +
						"Line 1: App key\n" +
						"Line 2: App secret\n",
						Dropbox.ApiKeyFile ),
					this.Title, MessageBoxButton.OK, MessageBoxImage.Warning );
				return;
			}

			if( !m_model.IsConnected )
			{
				m_model.LogIn();
				this.BtnLogin.Content = "_Log Out";
			}
			else
			{
				m_model.LogOut();
				this.BtnLogin.Content = LoginButtonText;
			}
		}

		private void BtnRefresh_Click( object sender, RoutedEventArgs e )
		{
			if( HasUnsavedChanges() )
				return;
			m_model.RefreshEntryList();
		}

		private void BtnNewEntry_Click( object sender, RoutedEventArgs e )
		{
			CommitEdits();
			m_model.CreateNewEntry();
			this.TextEntryContent.Focus();
		}

		private void BtnFirstEntry_Click( object sender, RoutedEventArgs e )
		{
			CommitEdits();
			m_model.MoveToFirst();
		}

		private void BtnPreviousEntry_Click( object sender, RoutedEventArgs e )
		{
			CommitEdits();
			m_model.MoveToPrevious();
		}

		private void BtnNextEntry_Click( object sender, RoutedEventArgs e )
		{
			CommitEdits();
			m_model.MoveToNext();
		}

		private void BtnLastEntry_Click( object sender, RoutedEventArgs e )
		{
			CommitEdits();
			m_model.MoveToLast();
		}

		private void CommitEdits()
		{
			this.TextEntryContent.GetBindingExpression( TextBox.TextProperty ).UpdateSource();
		}

		private bool HasUnsavedChanges()
		{
			if( !m_model.HasUnsavedChanges )
				return false;
			if( MessageBoxResult.OK != MessageBox.Show( this,
				"One or more journal entries has unsaved changes! Proceeding will discard these changes.",
				this.Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning ) )
			{
				return true;
			}
			return false;
		}

		private void SaveTimer( object state )
		{
			this.Dispatcher.Invoke( CommitEdits );
			m_model.AutoSave();
		}

		private void model_ErrorOccurred( object sender, string error )
		{
			if( !this.Dispatcher.CheckAccess() )
			{
				this.Dispatcher.BeginInvoke( new Action( () => model_ErrorOccurred( sender, error ) ) );
				return;
			}

			MessageBox.Show( this, error, this.Title, MessageBoxButton.OK, MessageBoxImage.Error );
		}
	}
}
