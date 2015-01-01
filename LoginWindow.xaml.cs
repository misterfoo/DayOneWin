namespace DayOneWin
{
	using System;
	using System.Web;
	using System.Windows;
	using System.Windows.Controls;
	using DropNet;
	using DropNet.Models;

	/// <summary>
	/// Interaction logic for LoginWindow.xaml
	/// </summary>
	public partial class LoginWindow : Window
	{
		public LoginWindow()
		{
			InitializeComponent();

			// Navigate to the login page.
			Dropbox db = Dropbox.Connect( token: null, rootPath: null );
			var authorizeUrl = db.RawClient.BuildAuthorizeUrl(
				DropNet.Authenticators.OAuth2AuthorizationFlow.Token, RedirectUri.AbsoluteUri );
			this.Browser.Navigate( authorizeUrl );
		}

		private static readonly Uri RedirectUri = new Uri( "https://www.dropbox.com/1/oauth2/redirect_receiver" );

		public string UserToken { get; set; }

		private void WebBrowser_Navigated( object sender, System.Windows.Navigation.NavigationEventArgs e )
		{
			HandleNavigation( e.Uri );
		}

		private void WebBrowser_Navigating( object sender, System.Windows.Navigation.NavigatingCancelEventArgs e )
		{
			HandleNavigation( e.Uri );
		}

		private void HandleNavigation( Uri url )
		{
			if( url.LocalPath != RedirectUri.LocalPath || this.UserToken != null )
				return;

			var parts = HttpUtility.ParseQueryString( url.Fragment.Substring( 1 ) );
			this.UserToken = parts["access_token"];
			System.Diagnostics.Debug.WriteLine( "Token is: " + this.UserToken );
			Close();
		}
	}
}
