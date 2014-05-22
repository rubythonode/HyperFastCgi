// Mono.WebServer.BaseApplicationHost
//
// Authors:
//	Gonzalo Paniagua Javier (gonzalo@ximian.com)
//	Lluis Sanchez Gual (lluis@ximian.com)
//
// (C) Copyright 2004 Novell, Inc
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
using System.Collections;
using System.IO;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Configuration;
using System.Collections.Generic;

namespace Mono.WebServer
{
	public class BaseApplicationHost : MarshalByRefObject, IApplicationHost1
	{
		string path;
		string vpath;
		EndOfRequestHandler endOfRequest;
		protected ApplicationServer appserver;

		public BaseApplicationHost ()
		{
			endOfRequest = new EndOfRequestHandler (EndOfRequest);
			AppDomain.CurrentDomain.DomainUnload += new EventHandler (OnUnload);
		}

		public void Unload ()
		{
			Console.WriteLine ("Appdomain unloaded");
			HttpRuntime.UnloadAppDomain ();
		}

		protected virtual void OnUnload (object o, EventArgs args)
		{
			Console.WriteLine ("Appdomain OnUnload");
			if (appserver != null)
				appserver.DestroyHost (this);
		}

		public override object InitializeLifetimeService ()
		{
			return null; // who wants to live forever?
		}

		public ApplicationServer Server {
			get { return appserver; }
			set { appserver = value; }
		}

		public string Path {
			get {
				if (path == null)
					path = AppDomain.CurrentDomain.GetData (".appPath").ToString ();

				return path;
			}
		}

		public string VPath {
			get {
				if (vpath == null)
					vpath = AppDomain.CurrentDomain.GetData (".appVPath").ToString ();

				return vpath;
			}
		}

		public AppDomain Domain {
			get { return AppDomain.CurrentDomain; }
		}

		protected void ProcessRequest (MonoWorkerRequest mwr)
		{
			if (mwr == null) {
				EndOfRequest (mwr);
				return;
			}
			
			if (!mwr.ReadRequestData ()) {
				EndOfRequest (mwr);
				return;
			}
			
			mwr.EndOfRequestEvent += endOfRequest;
			try {
				mwr.ProcessRequest ();
			} catch (ThreadAbortException) {
				Thread.ResetAbort ();
			} catch (Exception ex) { // should "never" happen
				// we don't know what the request state is,
				// better write the exception to the console
				// than forget it.
				Console.WriteLine ("Unhandled exception: {0}", ex);
				EndOfRequest (mwr);
			}
		}

		public void EndOfRequest (MonoWorkerRequest mwr)
		{
			try {
				mwr.CloseConnection ();
			} catch {
			} finally {
			}
		}
	}
}
