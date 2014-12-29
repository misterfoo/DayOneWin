namespace DayOneWin
{
	using System;
	using System.IO;
	using System.Windows;

	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		public App()
		{
			string appData = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
			StorageFolder = Path.Combine( appData, "Microsmurf", "DayOneWin" );
			Directory.CreateDirectory( StorageFolder );
		}

		public static string StorageFolder { get; set; }
	}
}
