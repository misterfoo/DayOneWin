namespace DayOneWin
{
	using System;
	using System.IO;
	using DropNet;
	using DropNet.Models;

	static class Dropbox
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
		/// Creates a DropNet client instance with the given user token.
		/// </summary>
		public static DropNetClient CreateClient( string token = null )
		{
			var c = new DropNetClient( AppKey, AppSecret, null, DropNetClient.AuthenticationMethod.OAuth2 );
			if( token != null )
			{
				UserLogin login = new UserLogin();
				login.Token = token;
				c.UserLogin = login;
			}
			return c;
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
