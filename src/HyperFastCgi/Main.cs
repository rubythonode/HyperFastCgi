//
// server.cs: Web Server that uses ASP.NET hosting
//
// Authors:
//   Sergey Zhukov 
//   Brian Nickel (brian.nickel@gmail.com)
//   Gonzalo Paniagua Javier (gonzalo@ximian.com)
//
// (C) 2002,2003 Ximian, Inc (http://www.ximian.com)
// (C) Copyright 2004 Novell, Inc. (http://www.novell.com)
// (C) Copyright 2007 Brian Nickel
// (C) Copyright 2013 Sergey Zhukov
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Web.Hosting;
using Mono.WebServer;
//using System.Configuration;
using Mono.Unix;
using Mono.Unix.Native;
using HyperFastCgi.Logging;
using HyperFastCgi.Sockets;
using System.Threading;
using HyperFastCgi.ApplicationServers;
using HyperFastCgi.Transports;
using HyperFastCgi.Listeners;
using System.Net.Sockets;
using System.Collections.Generic;
using HyperFastCgi.Configuration;
using HyperFastCgi.Interfaces;

namespace HyperFastCgi
{
	public class MainClass
	{
		static void ShowVersion ()
		{
			Assembly assembly = Assembly.GetExecutingAssembly ();
			string version = assembly.GetName ().Version.ToString ();
			object att;

			att = assembly.GetCustomAttributes (
				typeof(AssemblyCopyrightAttribute), false) [0];
			string copyright =
				((AssemblyCopyrightAttribute)att).Copyright;

			att = assembly.GetCustomAttributes (
				typeof(AssemblyDescriptionAttribute), false) [0];
			string description =
				((AssemblyDescriptionAttribute)att).Description;

			Console.WriteLine ("{0} {1}\n(c) {2}\n{3}",
				Path.GetFileName (assembly.Location), version,
				copyright, description);
		}

		static void ShowHelp ()
		{
			string name = Path.GetFileName (
				              Assembly.GetExecutingAssembly ().Location);

			ShowVersion ();
			Console.WriteLine ();
			Console.WriteLine ("Usage is:\n");
			Console.WriteLine ("    {0} [...]", name);
			Console.WriteLine ();
			configmanager.PrintHelp ();
		}

		private static ConfigurationManager configmanager;

		private static bool useThreadPool;

		public static int Main (string[] args)
		{
			// Load the configuration file stored in the
			// executable's resources.
			configmanager = new ConfigurationManager (
				typeof(MainClass).Assembly,
				"ConfigurationManager.xml");

			configmanager.LoadCommandLineArgs (args);

			// Show the help and exit.
			if ((bool)configmanager ["help"] ||
			    (bool)configmanager ["?"]) {
				ShowHelp ();
				return 0;
			}

			// Show the version and exit.
			if ((bool)configmanager ["version"]) {
				ShowVersion ();
				return 0;
			}

			string config = (string) configmanager ["config"];

			if (config == null) {
				Console.WriteLine ("You must pass /config=<filename> option. See 'help' for more info");
				return 1;
			}


			try {
				string config_file = (string)
				                     configmanager ["configfile"];
				if (config_file != null)
					configmanager.LoadXmlConfig (
						config_file);
			} catch (ApplicationException e) {
				Console.WriteLine (e.Message);
				return 1;
			} catch (System.Xml.XmlException e) {
				Console.WriteLine (
					"Error reading XML configuration: {0}",
					e.Message);
				return 1;
			}

			try {
				string log_level = (string)
				                   configmanager ["loglevels"];

				if (log_level != null)
					Logger.Level = (LogLevel)
					               Enum.Parse (typeof(LogLevel),
						log_level);
			} catch {
				Console.WriteLine ("Failed to parse log levels.");
				Console.WriteLine ("Using default levels: {0}",
					Logger.Level);
			}

			// Enable console logging during Main ().
			Logger.WriteToConsole = true;

			try {
				string log_file = (string)
				                  configmanager ["logfile"];

				if (log_file != null)
					Logger.Open (log_file);
			} catch (Exception e) {
				Logger.Write (LogLevel.Error,
					"Error opening log file: {0}",
					e.Message);
				Logger.Write (LogLevel.Error,
					"Events will not be logged.");
			}

			Logger.Write (LogLevel.Debug,
				Assembly.GetExecutingAssembly ().GetName ().Name);

			string root_dir = configmanager ["root"] as string;
			if (root_dir != null && root_dir.Length != 0) {
				try {
					Environment.CurrentDirectory = root_dir;
				} catch (Exception e) {
					Logger.Write (LogLevel.Error,
						"Error: {0}", e.Message);
					return 1;
				}
			}

			root_dir = Environment.CurrentDirectory;
			bool auto_map = false; //(bool) configmanager ["automappaths"];

			string applications = (string)
			                      configmanager ["applications"];
			string app_config_file;
			string app_config_dir;
			List<WebAppConfig> webapps = new List<WebAppConfig> ();

			try {
				app_config_file = (string)
				                  configmanager ["appconfigfile"];
				app_config_dir = (string)
				                 configmanager ["appconfigdir"];
			} catch (ApplicationException e) {
				Logger.Write (LogLevel.Error, e.Message);
				return 1;
			}

			if (config != null) {
				webapps.AddRange (ConfigUtils.GetApplicationsFromConfigFile (config));
			}

			if (applications != null) {
				webapps.AddRange (ConfigUtils.GetApplicationsFromCommandLine (applications));
			}

			if (app_config_file != null) {
				webapps.AddRange (ConfigUtils.GetApplicationsFromConfigFile (app_config_file));
			}

			if (app_config_dir != null) {
				webapps.AddRange (ConfigUtils.GetApplicationsFromConfigDirectory (app_config_dir));
			}

			if (webapps.Count==0 && !auto_map) {
				Logger.Write (LogLevel.Error,
					"There are no applications defined, and path mapping is disabled.");
				Logger.Write (LogLevel.Error,
					"Define an application using /applications, /appconfigfile, /appconfigdir");
				/*				
				Logger.Write (LogLevel.Error,
					"or by enabling application mapping with /automappaths=True.");
				*/
				return 1;
			}

			Logger.Write (LogLevel.Debug, "Root directory: {0}", root_dir);

			useThreadPool = (bool)configmanager ["usethreadpool"];

			string[] minThreads = ((string)configmanager ["minthreads"]).Split(',');
			string[] maxThreads = ((string)configmanager ["maxthreads"]).Split(',');
			int mintw=0, mintio=0, maxtw=0, maxtio=0;

			Int32.TryParse (minThreads [0], out mintw);
			if (minThreads.Length > 1)
				Int32.TryParse (minThreads [1], out mintio);

			Int32.TryParse (maxThreads [0], out maxtw);
			if (maxThreads.Length > 1)
				Int32.TryParse (maxThreads [1], out maxtio);

			SetThreads (mintw, mintio, maxtw, maxtio);


//			server.MaxConnections = (ushort)
//			                        configmanager ["maxconns"];
//			server.MaxRequests = (ushort)
//			                     configmanager ["maxreqs"];
//			server.MultiplexConnections = (bool)
//			                              configmanager ["multiplex"];

//			Logger.Write (LogLevel.Debug, "Max connections: {0}",
//				server.MaxConnections);
//			Logger.Write (LogLevel.Debug, "Max requests: {0}",
//				server.MaxRequests);
//			Logger.Write (LogLevel.Debug, "Multiplex connections: {0}",
//				server.MultiplexConnections);

			bool stopable = (bool)configmanager ["stopable"];
			Logger.WriteToConsole = (bool)configmanager ["printlog"];
//			host.LogLevel = Logger.Level;
//			host.LogToConsole = Logger.WriteToConsole;
//			host.AddTrailingSlash = (bool)configmanager ["addtrailingslash"];

			SimpleApplicationServer srv = new SimpleApplicationServer (root_dir);

			List<ConfigInfo> listenerConfigs = ConfigUtils.GetConfigsFromFile (config, "listener", typeof(ListenerConfig));
			if (listenerConfigs.Count != 1) {
				if (listenerConfigs.Count == 0) {
					Console.WriteLine ("Can't find <listener> node in file '{0}'", config); 
				} else {
					Console.WriteLine ("Only one listener are supported currently. Please remove redudant <listener> node from file '{0}'", config);
				}
				return 1;
			}
			List<ConfigInfo> hostConfigs = ConfigUtils.GetConfigsFromFile (config, "apphost", typeof(AppHostConfig));
			if (hostConfigs.Count == 0) {
				Console.WriteLine ("Can't find <apphost> node in file '{0}'", config);
				return 1;
			}

			IWebListener listener = (IWebListener)Activator.CreateInstance(listenerConfigs[0].Type);
			listener.Configure (listenerConfigs[0].Config, srv, 
				listenerConfigs[0].ListenerTransport != null? listenerConfigs[0].ListenerTransport.Type: null,
				listenerConfigs[0].ListenerTransport != null? listenerConfigs[0].ListenerTransport.Config: null,
				listenerConfigs[0].AppHostTransport != null? listenerConfigs[0].AppHostTransport.Type: null,
				listenerConfigs[0].AppHostTransport != null? listenerConfigs[0].AppHostTransport.Config: null
			);

			foreach (WebAppConfig appConfig in webapps) {
				srv.CreateApplicationHost (
					hostConfigs[0].Type, hostConfigs[0].Config,
					appConfig,
					listener.Transport, listener.AppHostTransportType, 
					listenerConfigs[0].AppHostTransport != null ? listenerConfigs[0].AppHostTransport.Config: null);
			}
			listener.Listen ();

			configmanager = null;

			if (stopable) {
				Console.WriteLine (
					"Hit Return to stop the server.");
				Console.ReadLine ();
			} else {
				UnixSignal[] signals = new UnixSignal[] { 
					new UnixSignal (Signum.SIGINT), 
					new UnixSignal (Signum.SIGTERM), 
				};

				// Wait for a unix signal
				for (bool exit = false; !exit;) {
					int id = UnixSignal.WaitAny (signals);

					if (id >= 0 && id < signals.Length) {
						if (signals [id].IsSet)
							exit = true;
					}
				}
			}

			return 0;
		}

		static void SetThreads(int minWorkerThreads, int minIOThreads, int maxWorkerThreads, int maxIOThreads)
		{
			if (minWorkerThreads ==0 &&  minIOThreads ==0 && maxWorkerThreads ==0 && maxIOThreads == 0)
				return;

			if ((maxWorkerThreads != 0 && maxWorkerThreads < minWorkerThreads) || (maxIOThreads != 0 && maxIOThreads < minIOThreads))
				throw new ArgumentException ("min threads must not be greater max threads");
			int minwth, minioth, maxwth, maxioth;

			ThreadPool.GetMinThreads (out minwth, out minioth);
			ThreadPool.GetMaxThreads (out maxwth, out maxioth);

			if (minWorkerThreads > minwth)
				minwth = minWorkerThreads;
			if (minIOThreads>minioth)
				minioth = minIOThreads;
			if (maxWorkerThreads != 0)
				maxwth = maxWorkerThreads;
			if (maxIOThreads != 0)
				maxioth = maxIOThreads;
			if (maxwth < minwth)
				maxwth = minwth;
			if (maxioth < minioth)
				maxioth = minioth;

			if (!ThreadPool.SetMaxThreads (maxwth, maxioth) ||
			    !ThreadPool.SetMinThreads (minwth, minioth))
				throw new Exception ("Could not set threads");

			Logger.Write (LogLevel.Debug, "Threadpool minw={0},minio={1},maxw={2},maxio={3}", minwth, minioth, maxwth, maxioth);
		}
	}
}
