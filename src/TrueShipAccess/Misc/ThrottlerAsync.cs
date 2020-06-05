﻿using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace TrueShipAccess.Misc
{
	public class ThrottlerAsync
	{
		private readonly int _maxQuota;
		private int _remainingQuota;
		private readonly Func< int, int > _releasedQuotaCalculator;
		private readonly Func< Task > _delay;
		private readonly int _maxRetryCount;

		private readonly SemaphoreSlim semaphore = new SemaphoreSlim( 1 );

		//TODO: Update delayInSeconds to milliseconds or change type to decimal
		public ThrottlerAsync( int maxQuota, int delayInSeconds ):
			this( maxQuota, el => el / delayInSeconds, () => Task.Delay( delayInSeconds * 1000 ), 10 )
		{
		}

		public ThrottlerAsync( int maxQuota, Func< int, int > releasedQuotaCalculator, Func< Task > delay, int maxRetryCount )
		{
			this._maxQuota = this._remainingQuota = maxQuota;
			this._releasedQuotaCalculator = releasedQuotaCalculator;
			this._delay = delay;
			this._maxRetryCount = maxRetryCount;
		}

		private const int TrueShipBucketSize = 1;
		private const int TrueShipDripRate = 1;

		// default throttler that implements TrueShip leaky bucket
		public ThrottlerAsync()
			: this( TrueShipBucketSize, elapsedTimeSeconds => elapsedTimeSeconds * TrueShipDripRate, () => Task.Delay( 1 * 1000 ), 20 )
		{
		}

		public async Task< TResult > ExecuteAsync< TResult >( Func< Task< TResult > > funcToThrottle )
		{
			var retryCount = 0;
			while( true )
			{
				var shouldWait = false;
				try
				{
					TrueShipLogger.Log().Debug( "Throttler: trying execute request for the {0} time", retryCount );
					return await this.TryExecuteAsync( funcToThrottle ).ConfigureAwait( false );
				}
				catch( Exception ex )
				{
					if( !this.IsExceptionFromThrottling( ex ) )
						throw;

					if( retryCount >= this._maxRetryCount )
						throw new ThrottlerException( "Throttle max retry count reached", ex );

					TrueShipLogger.Log().Debug( "Throttler: got throttling exception. Retrying..." );
					this._remainingQuota = 0;
					this._requestTimer.Restart();
					shouldWait = true;
					retryCount++;
					// try again through loop
				}
				if( shouldWait )
				{
					TrueShipLogger.Log().Debug( "Throttler: waiting before next retry..." );
					await this._delay();
				}
			}
		}

		private async Task< TResult > TryExecuteAsync< TResult >( Func< Task< TResult > > funcToThrottle )
		{
			await this.WaitIfNeededAsync();
			var result = await funcToThrottle();
			TrueShipLogger.Log().Debug( "Throttler: request executed successfully" );
			this.SubtractQuota();
			return result;
		}

		private bool IsExceptionFromThrottling( Exception exception )
		{
			if( !( exception is WebException ) )
				return false;
			var webException = ( WebException )exception;
			if( webException.Status != WebExceptionStatus.ProtocolError )
				return false;

			return webException.Response is HttpWebResponse && ( ( HttpWebResponse )webException.Response ).StatusCode == ( HttpStatusCode )429;
		}

		private async Task WaitIfNeededAsync()
		{
			await this.semaphore.WaitAsync();
			try
			{
				this.UpdateRequestQuoteFromTimer();

				if( this._remainingQuota != 0 )
					return;
			}
			finally
			{
				this.semaphore.Release();
			}

			TrueShipLogger.Log().Debug( "Throttler: quota exceeded. Waiting..." );
			await this._delay();
		}

		private async void SubtractQuota()
		{
			await this.semaphore.WaitAsync();
			try
			{
				this._remainingQuota--;
				if( this._remainingQuota < 0 )
					this._remainingQuota = 0;
			}
			finally
			{
				this.semaphore.Release();
			}

			this._requestTimer.Start();
			TrueShipLogger.Log().Debug( "Throttler: substracted quota, now available {0}", this._remainingQuota );
		}

		private void UpdateRequestQuoteFromTimer()
		{
			if( !this._requestTimer.IsRunning || this._remainingQuota == this._maxQuota )
				return;

			var totalSeconds = this._requestTimer.Elapsed.TotalSeconds;
			var elapsed = ( int )Math.Floor( totalSeconds );

			var quotaReleased = this._releasedQuotaCalculator( elapsed );

			TrueShipLogger.Log().Debug( "Throttler: {0} seconds elapsed, quota released: {1}", elapsed, quotaReleased );

			if( quotaReleased == 0 )
				return;

			this._remainingQuota = Math.Min( this._remainingQuota + quotaReleased, this._maxQuota );
			TrueShipLogger.Log().Debug( "Throttler: added quota, now available {0}", this._remainingQuota );

			this._requestTimer.Reset();
		}

		private readonly Stopwatch _requestTimer = new Stopwatch();

		public class ThrottlerException: Exception
		{
			public ThrottlerException()
			{
			}

			public ThrottlerException( string message )
				: base( message )
			{
			}

			public ThrottlerException( string message, Exception innerException )
				: base( message, innerException )
			{
			}
		}
	}
}