using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MastControlPandur3
{
	/// <summary>
	/// Wrapper für win32-API zu Multimedia-Timer.
	/// </summary>
	public class MMTimer
	{
		/// <summary>
		/// Defines constants for the multimedia Timer's event types.
		/// </summary>
		public enum TimerMode
		{
			/// <summary>
			/// Timer event occurs once.
			/// </summary>
			OneShot,

			/// <summary>
			/// Timer event occurs periodically.
			/// </summary>
			Periodic
		};
		// Represents the method that is called by Windows when a timer event occurs.
		private delegate void APITimeProc(uint id, uint msg, UserTimeProc userProc, UIntPtr param1, UIntPtr param2);

		// Delegate der User-CALLBACK-Funktione
		public delegate void UserTimeProc();

		/// <summary>
		/// Represents information about the multimedia Timer's capabilities.
		/// </summary>
		[StructLayout(LayoutKind.Sequential)]
		public struct TimerCaps
		{
			/// <summary>
			/// Minimum supported period in milliseconds.
			/// </summary>
			public uint periodMin;

			/// <summary>
			/// Maximum supported period in milliseconds.
			/// </summary>
			public uint periodMax;
		}

		// Gets timer capabilities.
		[DllImport("winmm.dll")]
		private static extern int timeGetDevCaps(ref TimerCaps caps, uint sizeOfTimerCaps);

		// Creates and starts the timer.
		[DllImport("winmm.dll")]
		private static extern int timeSetEvent(uint uTimerID, uint uMsg, APITimeProc apiProc, UserTimeProc userProc, uint fuEvent);

		// Stops and destroys the timer.
		[DllImport("winmm.dll")]
		private static extern int timeKillEvent(int id);

		// Indicates that the operation was successful.
		private const int TIMERR_NOERROR = 0;

		// Timer identifier.
		private int timerID;

		// Indicates whether or not the timer is running.
		private bool running = false;

		// Multimedia timer capabilities.
		private static TimerCaps caps;

		// Wichtig: Der Funktionszeiger muss gespeichert werden, sonst wird das
		// Objekt irgendwann weggeräumt!!!
		private static APITimeProc apiProc = new APITimeProc(apiTimeProc);

		/// <summary>
		/// Initialize class.
		/// </summary>
		static MMTimer()
		{
			// Get multimedia timer capabilities.
			timeGetDevCaps(ref caps, (uint)Marshal.SizeOf(caps));
		}

		/// <summary>
		/// Initializes a new instance of the Timer class.
		/// </summary>
		public MMTimer()
		{
			running = false;
		}

		~MMTimer()
		{
			if(IsRunning)
			{
				// Stop and destroy timer.
				timeKillEvent(timerID);
			}
		}

		/// <summary>
		/// Für timeSetEvent vorgesehene CALLBACK-Funktion mit passenden Parameter. Ruft einfach nur die
		/// userProc auf, kann daher statisch definiert werden. 
		/// </summary>
		private static void apiTimeProc(uint id, uint msg, UserTimeProc userProc, UIntPtr param1, UIntPtr param2)
		{
			userProc();
		}

		/// <summary>
		/// Einen Multimeldia-Timer starten.
		/// </summary>
		/// <param name="userProc">Benutzer CALLBACK-Funktion</param>
		/// <param name="period">Intervall in ms</param>
		/// <param name="mode">SingleShot/Periodisch?</param>
		/// <returns>0 bei Fehler, tatsächliches Intervall sonst</returns>
		public uint Start(UserTimeProc userProc, uint period, TimerMode mode)
		{
			if(IsRunning)
				return 0;

			// Abprüfen, ob Intervall im zulässigen Bereich
			if (period < caps.periodMin)
				period = caps.periodMin;
			if (period > caps.periodMax)
				period = caps.periodMax;

			// Create and start timer.
			timerID = timeSetEvent(period, 0, apiProc, userProc, (uint)mode);

			// If the timer was created successfully.
			if(timerID != 0)
			{
				running = true;
			}
			else
			{
				throw new System.Exception("Unable to start multimedia Timer.");
			}
			return period;
		}

		/// <summary>
		/// Multimedia-Timer anhalten.
		/// </summary>
		public void Stop()
		{
			if(!running)
			{
				return;
			}
			// Stop and destroy timer.
			int result = timeKillEvent(timerID);

			Debug.Assert(result == TIMERR_NOERROR);

			running = false;
		}        

		/// <summary>
		/// Gets a value indicating whether the Timer is running.
		/// </summary>
		public bool IsRunning
		{
			get
			{
				return running;
			}
		}

	}
}
