using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Topshelf;

namespace ProMinerPipeTunnel
{
	class Program
	{
		static int Main( string[] args )
		{
			foreach ( var arg in args )
			{
				if ( arg.StartsWith( "-h", StringComparison.InvariantCultureIgnoreCase ) ) return PrintHelp();
				if ( arg.StartsWith( "--help", StringComparison.InvariantCultureIgnoreCase ) ) return PrintHelp();
			}

			var config = Config.Read();

			HostFactory.Run( x =>
			{
				x.AddCommandLineDefinition( "private", s => { config.PrivatePipeName = s; } );
				x.AddCommandLineDefinition( "published", s => { config.PublishedPipeName = s; } );
				x.AddCommandLineDefinition( "sid", s => { config.SecurityId = s; } );

				x.Service<Controller>(
					s =>
					{
						s.ConstructUsing( name => new Controller( config ) );

						s.WhenStarted( ( c, hostControl ) => c.Start( hostControl ) );

						s.WhenStopped( ( c, hostControl ) => { try { c.Stop( hostControl, false ); } catch { } return true; } );

						s.WhenShutdown( ( c, hostControl ) => c.Stop( hostControl, true ) );
					}
				);

				x.RunAsLocalSystem();
				x.StartAutomatically();
				x.EnableShutdown();

				x.SetDescription( @"Pro Miner Pipe Tunnel" );
				x.SetDisplayName( @"Pro Miner Pipe Tunnel" );
				x.SetServiceName( @"ProMinerPipeTunnel" );
			} );

			return 0;
		}

		static int PrintHelp()
		{
			Console.WriteLine( "Usage: ProMinerPipeTunnel.exe [-option:value]..." );
			Console.WriteLine( "Options:" );
			Console.WriteLine( " -h | --help show this help message" );
			Console.WriteLine( " -private:[private pipe name]             default: geth.private.ipc" );
			Console.WriteLine( " -published:[published pipe name]         default: geth.ipc" );
			Console.WriteLine( " -sid:[published pipe security username]  default: null ( AuthenticatedUsers )" );

			return 0;
		}
	}

	class Controller
	{
		Config _Config = null;

		public Controller( Config config )
		{
			_Config = config;

			Console.WriteLine( "ProMinerPipeTunnel:" +
				" -private:" + _Config.PrivatePipeName +
				" -published:" + _Config.PublishedPipeName +
				" -sid:" + ( String.IsNullOrWhiteSpace( _Config.SecurityId ) ? "[AuthenticatedUsers]" : _Config.SecurityId )
			);
		}

		public bool Start( HostControl hostControl )
		{
			Task.Run( () => StandardInputMonitor( hostControl ) );

			StartServerMain();

			return true;
		}

		public bool Stop( HostControl hostControl, bool isShutdown )
		{
			return true;
		}

		async void StandardInputMonitor( HostControl hostControl )
		{
			try
			{
				using ( var stream = Console.OpenStandardInput() )
				using ( var cin = new StreamReader( stream ) )
				{
					for ( ;;)
					{
						var line = await cin.ReadLineAsync();

						if ( line == null ) return;

						if ( line.Equals( "exit", StringComparison.InvariantCultureIgnoreCase ) )
						{
							hostControl.Stop();

							return;
						}
					}
				}
			}
			catch { }
		}

		void StartServerMain()
		{
			Task.Run( () => ServerMain() );
		}

		long _ConnectionCount = 0;

		async void ServerMain()
		{
			if ( _Config.PrivatePipeName.Equals( _Config.PublishedPipeName, StringComparison.InvariantCultureIgnoreCase ) )
			{
				Console.WriteLine( "ERROR: ProMinerPipeTunnel: private and published pipe names are the same: " + _Config.PrivatePipeName );
				return;
			}

			var haveStartedNextListener = false;

			var connectionNumber = Interlocked.Increment( ref _ConnectionCount );

			try
			{
				using ( var pipePublic = CreateServerStream( _Config.PublishedPipeName ) )
				{
					await pipePublic.WaitForConnectionAsync();

					StartServerMain();

					haveStartedNextListener = true;

					using ( StreamReader srPublic = new StreamReader( pipePublic ) )
					using ( StreamWriter swPublic = new StreamWriter( pipePublic ) )
					{
						swPublic.AutoFlush = true;

						using ( var pipePrivate = CreateClientStream( _Config.PrivatePipeName ) )
						{
							pipePrivate.Connect( 10 * 1000 );

							using ( StreamReader srPrivate = new StreamReader( pipePrivate ) )
							using ( StreamWriter swPrivate = new StreamWriter( pipePrivate ) )
							{
								swPrivate.AutoFlush = true;

#pragma warning disable 4014 // not awaited
								Task.Run( () =>
								{
									var bufferPrivate = new char[ 10240 ];

									var countPrivate = 0;

									while ( ( countPrivate = srPrivate.Read( bufferPrivate, 0, bufferPrivate.Length ) ) > 0 )
									{
										var s = new String( bufferPrivate, 0, countPrivate );
#if DEBUG
										Console.WriteLine( connectionNumber.ToString( "N0" ) + " => " + s );
#endif
										swPublic.Write( s );
									}
								} );
#pragma warning restore 4014

								var bufferPublic = new char[ 10240 ];

								var countPublic = 0;

								while ( ( countPublic = srPublic.Read( bufferPublic, 0, bufferPublic.Length ) ) > 0 )
								{
									var s = new String( bufferPublic, 0, countPublic );
#if DEBUG
									Console.WriteLine( connectionNumber.ToString( "N0" ) + " <= " + s );
#endif
									swPrivate.Write( s );
								}
							}
						}
					}
				}
			}
			catch ( Exception x )
			{
				Console.WriteLine( "EXCEPTION: ProMinerPipeTunnel: " + x.Message );

				await Task.Delay( TimeSpan.FromSeconds( 1 ) );
			}
			finally
			{
				if ( !haveStartedNextListener ) StartServerMain();
			}
		}

		NamedPipeServerStream CreateServerStream( string PublicPipeName )
		{
			return new NamedPipeServerStream(
				PublicPipeName,
				PipeDirection.InOut,
				NamedPipeServerStream.MaxAllowedServerInstances,
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous,
				1024,
				1024,
				PipeSecurity
			);
		}

		NamedPipeClientStream CreateClientStream( string PrivatePipeName )
		{
			return new NamedPipeClientStream(
				".",
				PrivatePipeName,
				PipeDirection.InOut,
				PipeOptions.Asynchronous,
				TokenImpersonationLevel.None
			);
		}

		PipeSecurity PipeSecurity
		{
			get
			{
				var ps = new PipeSecurity();

				ps.AddAccessRule(
					new PipeAccessRule(
						new SecurityIdentifier(
							WindowsIdentity.GetCurrent().User.Value
						),
						PipeAccessRights.FullControl,
						AccessControlType.Allow
					)
				);

				if (
					String.IsNullOrWhiteSpace( _Config.SecurityId )
					||
					_Config.SecurityId.Equals( "AuthenticatedUsers", StringComparison.InvariantCultureIgnoreCase )
				)
				{
					ps.AddAccessRule(
						new PipeAccessRule(
							new SecurityIdentifier(
								WellKnownSidType.AuthenticatedUserSid,
								null
							),
							PipeAccessRights.ReadWrite,
							AccessControlType.Allow
						)
					);
				}
				else
				{
					var account = new NTAccount( _Config.SecurityId );

					var sid = (SecurityIdentifier) account.Translate( typeof( SecurityIdentifier ) );

					ps.AddAccessRule(
						new PipeAccessRule(
							sid,
							PipeAccessRights.ReadWrite,
							AccessControlType.Allow
						)
					);
				}

				return ps;
			}
		}
	}

	class Config
	{
		public static Config Read()
		{
			try
			{
				var config = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(
					File.ReadAllText(
						Path.Combine(
							Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location ),
							"ProMinerPipeTunnel.config"
						)
					)
				);

				config.IsFromConfigFile = true;

				return config;
			}
			catch
			{
				return Default;
			}
		}

		static Config Default
		{
			get
			{
				return new Config();
			}
		}

		[Newtonsoft.Json.JsonIgnore]
		public bool IsFromConfigFile { get; private set; } = false;

		public string PrivatePipeName { get; set; } = "geth.private.ipc";
		public string PublishedPipeName { get; set; } = "geth.ipc";
		public string SecurityId { get; set; } = null;
	}
}
