
namespace DayOneWin
{
	using System;

	/// <summary>
	/// ViewModel for an object which tracks the status of something.
	/// </summary>
	class StatusInfoViewModel : BindableBase
	{
		public StatusInfoViewModel( string messagePrefix = null )
		{
			m_messagePrefix = messagePrefix;
			this.ExpiresAfter = TimeSpan.FromSeconds( 3 );
			this.Status = string.Empty;
		}

		private readonly string m_messagePrefix;
		private bool m_sticky;
		private DateTimeOffset m_changeTime;

		/// <summary>
		/// The status message.
		/// </summary>
		public string Status
		{
			get { return m_status; }
			private set { SetProperty( ref m_status, value ); }
		}
		private string m_status;

		/// <summary>
		/// Sets the status message (non-sticky)
		/// </summary>
		public void Set( string messageFormat, params object[] args )
		{
			Set( false, messageFormat, args );
		}

		/// <summary>
		/// Sets the status message (non-sticky)
		/// </summary>
		public void SetSticky( string messageFormat, params object[] args )
		{
			Set( true, messageFormat, args );
		}

		/// <summary>
		/// Clears the status message.
		/// </summary>
		public void Clear()
		{
			this.Status = string.Empty;
		}

		/// <summary>
		/// Gets or sets the display duration of non-sticky status messages.
		/// </summary>
		public TimeSpan ExpiresAfter { get; set; }

		/// <summary>
		/// Expires (clears) the current status message if appropriate.
		/// </summary>
		public void MaybeExpire()
		{
			if( m_status == string.Empty || m_sticky )
				return;
			var elapsed = (DateTimeOffset.UtcNow - m_changeTime);
			if( elapsed < this.ExpiresAfter )
				return;
			this.Status = string.Empty;
		}

		private void Set( bool sticky, string messageFormat, params object[] args )
		{
			string message = (args != null && args.Length > 0) ?
				string.Format( messageFormat, args ) : messageFormat;
			if( m_messagePrefix != null )
				message = m_messagePrefix + message;
			this.Status = message;
			m_sticky = sticky;
			m_changeTime = DateTimeOffset.UtcNow;
		}
	}
}
