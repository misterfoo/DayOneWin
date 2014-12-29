namespace DayOneWin
{
	using System;
	using System.Collections.Generic;
	using System.Runtime.Serialization;
	using System.Xml;

	class PropertyList
	{
		public class PList
		{
			public PList()
			{
				this.Entries = new List<KeyValuePair<string, object>>();
			}

			public List<KeyValuePair<string, object>> Entries { get; private set; }

			/// <summary>
			/// Adds an item to our list of entries.
			/// </summary>
			public void Add( string key, object value )
			{
				if( value == null )
					value = string.Empty;
				this.Entries.Add( new KeyValuePair<string, object>( key, value ) );
			}

			/// <summary>
			/// Converts our list of entries to a dictionary.
			/// </summary>
			public Dictionary<string, object> ToDictionary()
			{
				var dict = new Dictionary<string, object>();
				foreach( var pair in this.Entries )
					dict[pair.Key] = pair.Value;
				return dict;
			}
		}

		/// <summary>
		/// Gets the preferred reader settings for loading property lists.
		/// </summary>
		public static XmlReaderSettings XmlReaderSettings
		{
			get
			{
				XmlReaderSettings settings = new XmlReaderSettings();
				settings.DtdProcessing = DtdProcessing.Ignore;
				return settings;
			}
		}

		/// <summary>
		/// Gets the preferred writer settings for loading property lists.
		/// </summary>
		public static XmlWriterSettings XmlWriterSettings
		{
			get
			{
				XmlWriterSettings settings = new XmlWriterSettings();
				settings.NewLineChars = "\n";
				settings.Indent = true;
				return settings;
			}
		}

		/// <summary>
		/// Reads a property list from XML form
		/// </summary>
		public static object Read( XmlReader reader )
		{
			if( !reader.ReadToFollowing( Tags.PList ) ||
				reader.GetAttribute( "version" ) != "1.0" )
				throw new SerializationException( "Unable to read plist (missing root node or unknown version)" );
			ReadToNextElement( reader );
			return ReadPropertyListValue( reader );
		}

		/// <summary>
		/// Writes a property list to XML form
		/// </summary>
		public static void Write( XmlWriter writer, object value )
		{
			writer.WriteStartDocument();
			writer.WriteStartElement( Tags.PList );
			writer.WriteAttributeString( Tags.Version, KnownVersion );
			WritePropertyListValue( writer, value );
			writer.WriteEndElement();
			writer.WriteEndDocument();
		}

		private static void WritePropertyListValue( XmlWriter writer, object value )
		{
			if( value is string )
			{
				writer.WriteElementString( Tags.String, (string)value );
			}
			else if( value is DateTimeOffset )
			{
				string raw = ((DateTimeOffset)value).ToUniversalTime().ToString( @"yyyy-MM-dd\THH:mm:ss\Z" );
				writer.WriteElementString( Tags.Date, raw );
			}
			else if( value is bool )
			{
				bool b = (bool)value;
				if( b )
					writer.WriteElementString( Tags.True, string.Empty );
				else
					writer.WriteElementString( Tags.False, string.Empty );
			}
			else if( value is List<object> )
			{
				List<object> objs = (List<object>)value;
				writer.WriteStartElement( Tags.Array );
				foreach( object o in objs )
					WritePropertyListValue( writer, o );
				writer.WriteEndElement();
			}
			else if( value is PList )
			{
				PList nested = (PList)value;
				writer.WriteStartElement( Tags.Dict );
				foreach( var pair in nested.Entries )
				{
					writer.WriteElementString( Tags.Key, pair.Key );
					WritePropertyListValue( writer, pair.Value );
				}
				writer.WriteEndElement();
			}
			else if( value == null )
			{
				throw new SerializationException( "Can't serialize a null value" );
			}
			else
			{
				throw new SerializationException( "Not sure how to serialize " + value.GetType().Name );
			}
		}

		private static PList ReadPropertyList( XmlReader reader )
		{
			PList list = new PList();
			while( reader.Name == Tags.Key )
			{
				string key = reader.ReadElementContentAsString();
				ReadToNextElement( reader );
				object value = ReadPropertyListValue( reader );
				list.Entries.Add( new KeyValuePair<string, object>( key, value ) );
				ReadToNextElementOrCloseTag( reader );
			}
			return list;
		}

		private static object ReadPropertyListValue( XmlReader reader )
		{
			if( reader.LocalName == Tags.String )
			{
				return reader.ReadElementContentAsString();
			}
			else if( reader.LocalName == Tags.Date )
			{
				return DateTimeOffset.Parse( reader.ReadElementContentAsString() );
			}
			else if( reader.LocalName == Tags.False )
			{
				return false;
			}
			else if( reader.LocalName == Tags.True )
			{
				return true;
			}
			else if( reader.LocalName == Tags.Array )
			{
				var values = new List<object>();
				if( reader.IsEmptyElement )
					return values;
				ReadToNextElementOrCloseTag( reader );
				while( reader.NodeType != XmlNodeType.EndElement )
				{
					values.Add( ReadPropertyListValue( reader ) );
					ReadToNextElementOrCloseTag( reader );
				}
				return values;
			}
			else if( reader.LocalName == Tags.Dict )
			{
				// move to first content node
				ReadToNextElement( reader );

				return ReadPropertyList( reader );
			}
			else
			{
				throw new SerializationException( string.Format(
					"Unable to read plist (not sure what to do with {0})", reader.LocalName ) );
			}
		}

		private static void ReadToNextElement( XmlReader reader )
		{
			do
			{
				reader.Read();
			}
			while( reader.NodeType != XmlNodeType.Element );
		}

		private static void ReadToNextElementOrCloseTag( XmlReader reader )
		{
			do
			{
				reader.Read();
			}
			while( reader.NodeType != XmlNodeType.Element &&
				reader.NodeType != XmlNodeType.EndElement );
		}

		private static class Tags
		{
			public const string PList = "plist";
			public const string Version = "version";
			public const string Key = "key";
			public const string Dict = "dict";
			public const string Array = "array";
			public const string String = "string";
			public const string True = "true";
			public const string False = "false";
			public const string Date = "date";
		}

		private const string KnownVersion = "1.0";
	}
}
