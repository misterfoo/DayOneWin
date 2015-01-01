namespace DayOneWin
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.IO;
	using System.Linq;
	using System.Xml;

	/// <summary>
	/// In-memory representation of a journal entry.
	/// </summary>
	class JournalEntry : BindableBase
	{
		public JournalEntry()
		{
			this.Tags = new ObservableCollection<string>();
			this.Tags.CollectionChanged +=
				(s,a) => this.IsDirty = true;
		}

		private DateTimeOffset m_createdOn;
		private CreatorInfo m_creator;
		private string m_content;
		private bool m_starred;
		private string m_timeZone;

		/// <summary>
		/// The header string for the selected entry.
		/// </summary>
		public string HeaderString
		{
			get { return this.CreatedOn.LocalDateTime.ToString( "yyyy/MM/dd h:mm tt" ); }
		}

		public DateTimeOffset CreatedOn
		{
			get { return m_createdOn; }
			set { SetProperty( ref m_createdOn, value ); }
		}

		public CreatorInfo Creator
		{
			get { return m_creator; }
			set { SetProperty( ref m_creator, value ); }
		}

		public string Content
		{
			get { return m_content; }
			set { SetProperty( ref m_content, value ); }
		}

		public ObservableCollection<string> Tags { get; private set; }

		public bool Starred
		{
			get { return m_starred; }
			set { SetProperty( ref m_starred, value ); }
		}

		public string TimeZone
		{
			get { return m_timeZone; }
			set { SetProperty( ref m_timeZone, value ); }
		}

		public Guid Uuid
		{
			get; private set;
		}

		public bool EverBeenSaved
		{
			get { return (this.Uuid != Guid.Empty); }
		}

		/// <summary>
		/// Gets the leaf file name which should be used for this entry.
		/// </summary>
		public string DataFileName
		{
			get { return GetDataFileName( this.Uuid ); }
		}

		/// <summary>
		/// Gets the leaf file name which should be used for the entry with the given id.
		/// </summary>
		public static string GetDataFileName( Guid uuid )
		{
			return GuidToString( uuid ) + StandardFileExtension;
		}

		/// <summary>
		/// Loads a journal entry from obscured disk form.
		/// </summary>
		public static JournalEntry Load( string file )
		{
			using( var reader = XmlReader.Create( file, PropertyList.XmlReaderSettings ) )
				return FromPList( (PropertyList.PList)PropertyList.Read( reader ) );
		}

		/// <summary>
		/// Loads a journal entry from raw byte form.
		/// </summary>
		public static JournalEntry Load( byte[] bytes )
		{
			using( var stream = new System.IO.MemoryStream( bytes ) )
			using( var reader = XmlReader.Create( stream, PropertyList.XmlReaderSettings ) )
				return FromPList( (PropertyList.PList)PropertyList.Read( reader ) );
		}

		/// <summary>
		/// Saves a journal entry to obscured disk form.
		/// </summary>
		public void Save( string directory )
		{
			if( !this.EverBeenSaved )
				this.Uuid = Guid.NewGuid();
			string file = Path.Combine( directory, this.DataFileName );
			using( var writer = XmlWriter.Create( file, PropertyList.XmlWriterSettings ) )
				PropertyList.Write( writer, ToPlist() );
			this.IsDirty = false;
		}

		/// <summary>
		/// Gets the byte representation of a journal entry, suitable for upload to Dropbox.
		/// This undoes any obfuscation/compression/etc. which applies to the on-disk file.
		/// </summary>
		public static byte[] GetCleanBytes( string file )
		{
			throw new NotImplementedException();
		}

		public const string StandardFileExtension = ".doentry";

		public class CreatorInfo : BindableBase
		{
			public string DeviceAgent
			{
				get { return m_deviceAgent; }
				set { SetProperty( ref m_deviceAgent, value ); }
			}

			public DateTimeOffset GeneratedOn
			{
				get { return m_generatedOn; }
				set { SetProperty( ref m_generatedOn, value ); }
			}

			public string HostName
			{
				get { return m_hostName; }
				set { SetProperty( ref m_hostName, value ); }
			}

			public string OSAgent
			{
				get { return m_osAgent; }
				set { SetProperty( ref m_osAgent, value ); }
			}

			public string SoftwareAgent
			{
				get { return m_softwareAgent; }
				set { SetProperty( ref m_softwareAgent, value ); }
			}

			public static CreatorInfo FromPList( PropertyList.PList plist )
			{
				var dict = plist.ToDictionary();
				CreatorInfo info = new CreatorInfo();
				info.m_deviceAgent = ReadOne<string>( dict, "Device Agent" );
				info.m_generatedOn = ReadOne<DateTimeOffset>( dict, "Generation Date" );
				info.m_hostName = ReadOne<string>( dict, "Host Name" );
				info.m_osAgent = ReadOne<string>( dict, "OS Agent" );
				info.m_softwareAgent = ReadOne<string>( dict, "Software Agent" );
				return info;
			}

			public PropertyList.PList ToPlist()
			{
				PropertyList.PList plist = new PropertyList.PList();
				plist.Add( "Device Agent", m_deviceAgent );
				plist.Add( "Generation Date", m_generatedOn );
				plist.Add( "Host Name", m_hostName );
				plist.Add( "OS Agent", m_osAgent );
				plist.Add( "Software Agent", m_softwareAgent );
				return plist;
			}

			private string m_deviceAgent;
			private DateTimeOffset m_generatedOn;
			private string m_hostName;
			private string m_osAgent;
			private string m_softwareAgent;
		}

		/// <summary>
		/// Fills out the creator info and generation timestamp.
		/// </summary>
		public void SetCreationInfo()
		{
			this.Creator = new CreatorInfo()
			{
				DeviceAgent = "Desktop",
				GeneratedOn = DateTimeOffset.UtcNow,
				HostName = System.Environment.MachineName,
				OSAgent = System.Environment.OSVersion.VersionString,
				SoftwareAgent = "DayOneWin"
			};
			this.CreatedOn = this.Creator.GeneratedOn;

			TimeZone tz = System.TimeZone.CurrentTimeZone;
			this.TimeZone = tz.IsDaylightSavingTime( this.CreatedOn.LocalDateTime ) ?
				tz.DaylightName : tz.StandardName;
		}

		public static JournalEntry SampleEntry
		{
			get
			{
				var e = new JournalEntry()
				{
					Tags = new ObservableCollection<string>( new string[] { "stuff", "things" } ),
					Content = "Lorem ipsum",
					TimeZone = "Central",
					Uuid = new Guid( "e3b45feb-c526-4187-8f7a-8a63392f1445" )
				};
				e.SetCreationInfo();
				e.CreatedOn = e.Creator.GeneratedOn = DateTimeOffset.Now.AddHours( -1 );
				e.IsDirty = false;
				return e;
			}
		}

		private static JournalEntry FromPList( PropertyList.PList plist )
		{
			var dict = plist.ToDictionary();
			JournalEntry e = new JournalEntry();
			e.CreatedOn = ReadOne<DateTimeOffset>( dict, "Creation Date" );
			e.Creator = CreatorInfo.FromPList( ReadOne<PropertyList.PList>( dict, "Creator" ) );
			e.Content = ReadOne<string>( dict, "Entry Text" );
			var tags = ReadOne<List<object>>( dict, "Tags", new List<object>() ).Select( x => (string)x );
			foreach( string t in tags )
				e.Tags.Add( t );
			e.Starred = ReadOne<bool>( dict, "Starred" );
			e.TimeZone = ReadOne<string>( dict, "Time Zone" );
			e.Uuid = new Guid( ReadOne<string>( dict, "UUID" ) );
			e.IsDirty = false;
			return e;
		}

		private static TValue ReadOne<TValue>( Dictionary<string, object> values, string key, TValue defVal = default(TValue) )
		{
			object value;
			if( !values.TryGetValue( key, out value ) )
				return defVal;
			return (TValue)value;
		}

		private PropertyList.PList ToPlist()
		{
			PropertyList.PList plist = new PropertyList.PList();
			plist.Add( "Creation Date", this.CreatedOn );
			plist.Add( "Creator", this.Creator.ToPlist() );
			plist.Add( "Entry Text", this.Content );
			plist.Add( "Tags", this.Tags.Cast<object>().ToList() );
			plist.Add( "Starred", this.Starred );
			plist.Add( "Time Zone", this.TimeZone );
			plist.Add( "UUID", GuidToString( this.Uuid ) );
			return plist;
		}

		private static string GuidToString( Guid guid )
		{
			return guid.ToString( "N" ).ToUpper();
		}
	}
}
