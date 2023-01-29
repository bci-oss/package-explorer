﻿/*
Copyright (c) 2018-2021 Festo AG & Co. KG <https://www.festo.com/net/de_de/Forms/web/contact_international>
Author: Michael Hoffmeister

Copyright (c) 2019-2021 PHOENIX CONTACT GmbH & Co. KG <opensource@phoenixcontact.com>,
author: Andreas Orzelski

This source code is licensed under the Apache License 2.0 (see LICENSE.txt).

This source code may use other Open Source software components (see LICENSE.txt).
*/

// resharper disable EmptyEmbeddedStatement
// resharper disable FunctionNeverReturns
// resharper disable UnusedVariable
// resharper disable TooWideLocalVariableScope
// resharper disable EmptyConstructor

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AasxIntegrationBase;
using AasxPackageLogic;
using AasxPackageLogic.PackageCentral;
using AdminShellNS;
using BlazorExplorer;
using BlazorUI.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace AnyUi
{

    public enum AnyUiHtmlEventType { None, Dialog, ContextMenu }

    public class AnyUiHtmlEventSession
    {
        /// <summary>
        /// The session id matches the one in the BlazorSession.
        /// </summary>
        public int SessionId = 0;

        /// <summary>
        /// JavaScript runtime to call scripts.
        /// </summary>
		public IJSRuntime JsRuntime;

		/// <summary>
		/// Distincts the different working mode of display and
		/// execution of the event loop.
		/// </summary>
		public AnyUiHtmlEventType EventType;

		/// <summary>
		/// If <c>true</c>, a special HTML dialog rendering will occur for
		/// the event.
		/// </summary>
		public bool EventOpen;

        /// <summary>
        /// Will be raised if the modal event shall be closed.
        /// </summary>
        public bool EventDone;

        /// <summary>
        /// If a dialog shall be executed, dialogue data
        /// </summary>
        public AnyUiDialogueDataBase DialogueData;

        /// If a special action shall be executed, data to it
        /// </summary>
        public AnyUiSpecialActionBase SpecialAction;

        // old stuff

        public bool htmlDotnetEventIn = false;
        public bool htmlDotnetEventOut = false;
        public string htmlDotnetEventType = "";
        public List<object> htmlDotnetEventInputs = new List<object>();
        public List<object> htmlDotnetEventOutputs = new List<object>();

        public bool htmlEventIn = false;
        public bool htmlEventOut = false;
        public string htmlEventType = "";
        public List<object> htmlEventInputs = new List<object>();
        public List<object> htmlEventOutputs = new List<object>();

        /// <summary>
        /// Denotes, which event type leads to a special HTML rendering.
        /// </summary>
        // public AnyUiHtmlEventType EventType;

        // constructor: guarantee some data

        public AnyUiHtmlEventSession(int sessionId)
        {
            SessionId = sessionId;
        }

		// service function

		/// <summary>
		/// Will set all important flags in a way, that the event can be executed.
		/// </summary>
		/// <param name="diaData"></param>
		public T StartModal<T>(T diaData) where T : AnyUiDialogueDataBase
        {
            if (diaData == null)
            {
                EventOpen = false;
                EventDone = true;
                return diaData;
            }

            EventOpen = true;
            DialogueData = diaData;
            SpecialAction = null;
            EventDone = false;
            return diaData;
        }

		public T StartModalSpecialAction<T>(T sdData) where T : AnyUiSpecialActionBase
		{
			if (sdData == null)
			{
				EventOpen = false;
				EventDone = true;
				return sdData;
			}

			EventOpen = true;
            DialogueData = null;
			SpecialAction = sdData;
			EventDone = false;
			return sdData;
		}

		public void EndModal(bool result)
        {
			EventOpen = false;
			EventDone = true;
            if (DialogueData != null)
                DialogueData.Result = result;
		}
    }

    public enum AnyUiHtmlFillMode { None, FillWidth }

    public class AnyUiDisplayDataHtml : AnyUiDisplayDataBase
    {
        [JsonIgnore]
        public AnyUiDisplayContextHtml _context;

        [JsonIgnore]
        public ComponentBase _component;

        [JsonIgnore]
        public Action<AnyUiUIElement> TouchLambda;

        public AnyUiDisplayDataHtml(
            AnyUiDisplayContextHtml context,
            Action<AnyUiUIElement> touchLambda = null)
        {
            _context = context;
            if (touchLambda != null)
                TouchLambda = touchLambda;
        }

        public double GetScale() => 1.4;

        public static bool DebugFrames = false;
    }

    public class AnyUiDisplayContextHtml : AnyUiContextBase
    {
        [JsonIgnore]
        public PackageCentral Packages;

        [JsonIgnore]
        public BlazorUI.Data.BlazorSession _bi;

        [JsonIgnore]
        public IJSRuntime _jsRuntime;

        public AnyUiDisplayContextHtml(
            PackageCentral packageCentral,
            BlazorUI.Data.BlazorSession bi)
        {
            _bi = bi;
            Packages = packageCentral;
        }

        public void SetJsRuntime(IJSRuntime runtime)
        {
            _jsRuntime = runtime;
        }

        object htmlDotnetLock = new object();
        static object staticHtmlDotnetLock = new object();
        public static List<AnyUiHtmlEventSession> sessions = new List<AnyUiHtmlEventSession>();

        public static void AddEventSession(int sessionId)
        {
            lock (staticHtmlDotnetLock)
            {
                var s = new AnyUiHtmlEventSession(sessionId);
                sessions.Add(s);
            }
        }
        public static void DeleteEventSession(int sessionNumber)
        {
            lock (staticHtmlDotnetLock)
            {
                AnyUiHtmlEventSession found = null;
                foreach (var s in sessions)
                {
                    if (s.SessionId == sessionNumber)
                    {
                        found = s;
                        break;
                    }
                }
                if (found != null)
                    sessions.Remove(found);
            }
        }
        public static AnyUiHtmlEventSession FindEventSession(int sessionNumber)
        {
            AnyUiHtmlEventSession found = null;
            lock (staticHtmlDotnetLock)
            {
                foreach (var s in sessions)
                {
                    if (s.SessionId == sessionNumber)
                    {
                        found = s;
                        break;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// This very special suspicious double-mangled infinite loop serves as a context
        /// for dedicated event loops aside from the general Blazor event loop,
        /// that is: context menu, execution of value lambdas.
        /// This function seems to cyclically scan the event loop "sessions" and will
        /// "lock on" an active one and do some modal things ..
        /// TODO: Check if this would work with multiple users (!!!)
        /// </summary>
        public static void htmlDotnetLoop()
        {
            AnyUiUIElement el;

            while (true)
            {
                // ReSharper disable InconsistentlySynchronizedField
                int i = 0;
                while (i < sessions.Count)
                {
                    var s = sessions[i];
                    if (s.htmlDotnetEventIn)
                    {
                        switch (s.htmlDotnetEventType)
                        {
                            case "setValueLambda":
                                if (s.htmlDotnetEventInputs != null && s.htmlDotnetEventInputs.Count > 0)
                                {
                                    el = (AnyUiUIElement)s.htmlDotnetEventInputs[0];
                                    object o = s.htmlDotnetEventInputs[1];
                                    s.htmlDotnetEventIn = false;
                                    s.htmlDotnetEventInputs.Clear();
                                    var ret = el?.setValueLambda?.Invoke(o);

                                    while (s.htmlDotnetEventOut) Task.Delay(1);

                                    // determine, which (visual) update has to be done
                                    int ndm = 2;
                                    if (ret is AnyUiLambdaActionNone)
                                        ndm = 0;
                                    Program.signalNewData(
                                        new Program.NewDataAvailableArgs(
                                            Program.DataRedrawMode.SomeStructChange, s.SessionId, 
                                            newLambdaAction: ret, onlyUpdatePanel: true)); // build new tree
                                }
                                break;

                            case "contextMenu":
                                if (s.htmlDotnetEventInputs != null && s.htmlDotnetEventInputs.Count > 0)
                                {
                                    el = (AnyUiUIElement)s.htmlDotnetEventInputs[0];
                                    AnyUiSpecialActionContextMenu cntlcm = (AnyUiSpecialActionContextMenu)
                                        s.htmlDotnetEventInputs[1];
                                    s.htmlEventType = "contextMenu";
                                    s.htmlEventInputs.Add(el);
                                    s.htmlEventInputs.Add(cntlcm);
                                    s.htmlDotnetEventIn = false;
                                    s.htmlDotnetEventInputs.Clear();
                                    s.htmlEventIn = true;
                                    Program.signalNewData(
                                        new Program.NewDataAvailableArgs(Program.DataRedrawMode.SomeStructChange, s.SessionId,
                                        onlyUpdatePanel: true)); // same tree, but structure may change

                                    while (!s.htmlEventOut) Task.Delay(1);
                                    int bufferedI = 0;
                                    if (s.htmlEventOutputs.Count == 1)
                                    {
                                        bufferedI = (int)s.htmlEventOutputs[0];
                                        var action2 = cntlcm.MenuItemLambda?.Invoke(bufferedI);
                                    }
                                    s.htmlEventOutputs.Clear();
                                    s.htmlEventType = "";
                                    s.htmlEventOut = false;
                                    //// AnyUiLambdaActionBase ret = el.setValueLambda?.Invoke(o);
                                }
                                break;
                        }
                    }

                    if (s.EventType == AnyUiHtmlEventType.ContextMenu)
                    {
                        // assumption: context menu already on the screen
                        if (!s.EventOpen || !(s.SpecialAction is AnyUiSpecialActionContextMenu sacm))
                        {
                            // emergency exit
                            s.JsRuntime?.InvokeVoidAsync("blazorCloseModalForce");
                            return;
                        }

                        // simply do event loop (but here in the "background")
                        while (!s.EventDone) Task.Delay(1);
                        s.EventOpen = false;
                        s.EventDone = false;
                        s.EventType = AnyUiHtmlEventType.None;

                        // trigger display(again)
                        //Program.signalNewData(
                        //    new Program.NewDataAvailableArgs(
                        //        Program.DataRedrawMode.None, evs.SessionId));
                        s.JsRuntime?.InvokeVoidAsync("blazorCloseModalForce");

                        // directly concern about the results
                        if (sacm.ResultIndex >= 0)
						{
							int bufferedI = sacm.ResultIndex;
							var action2 = sacm.MenuItemLambda?.Invoke(bufferedI);
						}
					}

					i++;
                }
                // ReSharper enable InconsistentlySynchronizedField
                Thread.Sleep(100);
            }
        }

        public static void setValueLambdaHtml(AnyUiUIElement el, object o)
        {
            var dc = (el?.DisplayData as AnyUiDisplayDataHtml)?._context;
            if (dc != null)
            {
                var sessionNumber = dc._bi.SessionId;
                var found = FindEventSession(sessionNumber);
                if (found != null)
                {
                    lock (dc.htmlDotnetLock)
                    {
                        while (found.htmlDotnetEventIn) Task.Delay(1);
                        found.htmlEventInputs.Clear();
                        found.htmlDotnetEventType = "setValueLambda";
                        found.htmlDotnetEventInputs.Add(el);
                        found.htmlDotnetEventInputs.Add(o);
                        found.htmlDotnetEventIn = true;
                    }
                }
            }
        }

        public static void specialActionContextMenuHtml(
            AnyUiUIElement el, 
            AnyUiSpecialActionContextMenu cntlcm,
            AnyUiDisplayContextHtml context)
        {
            var dc = (el.DisplayData as AnyUiDisplayDataHtml)?._context;
            if (dc != null)
            {
                var sessionNumber = dc._bi.SessionId;
                var evs = FindEventSession(sessionNumber);
                if (evs != null)
                {
                    lock (dc.htmlDotnetLock)
                    {
                        // woodoo? -> wait for "old" dislogs????
                        // while (/* evs.htmlDotnetEventIn || evs.EventOpen) Task.Delay(1);

                        // start new thing
                        //evs.htmlEventInputs.Clear();
                        //evs.htmlDotnetEventType = "contextMenu";
                        //evs.htmlDotnetEventInputs.Add(el);
                        //evs.htmlDotnetEventInputs.Add(cntlcm);
                        //evs.htmlDotnetEventIn = true;

                        // simply treat woodoo as error!
                        evs.JsRuntime = dc._jsRuntime;

						if (evs.EventOpen)
					    {
						    Log.Singleton.Error("Error in starting special action as some modal dialogue " +
							    "is still active. Aborting!!");
						    return;
					    }

                        evs.EventType = AnyUiHtmlEventType.ContextMenu;
					    evs.StartModalSpecialAction(cntlcm);

					    // trigger display
					    Program.signalNewData(
						    new Program.NewDataAvailableArgs(
							    Program.DataRedrawMode.None, evs.SessionId));

                        // wait modal
                        //while (!evs.EventDone) Task.Delay(1);
                        //evs.EventOpen = false;
                        //evs.EventDone = false;

                        // trigger display (again)
                        //Program.signalNewData(
                        //    new Program.NewDataAvailableArgs(
                        //        Program.DataRedrawMode.None, evs.SessionId));
                        // context?._jsRuntime.InvokeVoidAsync("blazorCloseModalForce");

                    }

                }
            }
        }

        /// <summary>
        /// Show MessageBoxFlyout with contents
        /// </summary>
        /// <param name="message">Message on the main screen</param>
        /// <param name="caption">Caption string (title)</param>
        /// <param name="buttons">Buttons according to WPF standard messagebox</param>
        /// <param name="image">Image according to WPF standard messagebox</param>
        /// <returns></returns>
        public override AnyUiMessageBoxResult MessageBoxFlyoutShow(
            string message, string caption, AnyUiMessageBoxButton buttons, AnyUiMessageBoxImage image)
        {
            AnyUiMessageBoxResult r = AnyUiMessageBoxResult.None;
            var evs = FindEventSession(_bi.SessionId);
            if (evs == null)
                return r;

            var dd = evs.StartModal(new AnyUiDialogueDataMessageBox(
                caption, message, buttons, image));

            //evs.htmlEventInputs.Clear();
            //evs.htmlEventType = "MessageBoxFlyoutShow";
            //evs.htmlEventInputs.Add(message);
            //evs.htmlEventInputs.Add(caption);
            //evs.htmlEventInputs.Add(buttons);
            //evs.htmlEventIn = true;

            // trigger display
            Program.signalNewData(
                new Program.NewDataAvailableArgs(
                    Program.DataRedrawMode.None, evs.SessionId)); 

            // wait modal
            while (!evs.EventDone) Task.Delay(1);
            evs.EventDone = false;

            // dialog result
            if (dd.Result)
                r = dd.ResultButton;

            //evs.htmlEventType = "";
            //evs.htmlEventOutputs.Clear();
            //evs.htmlEventOut = false;
            //evs.htmlEventInputs.Clear();
            //evs.htmlDotnetEventIn = false;

            return r;
        }

		/// <summary>
		/// Shows an open file dialogue
		/// </summary>
		/// <param name="caption">Top caption of the dialogue</param>
		/// <param name="message">Further information to the user</param>
		/// <param name="filter">Filter specification for certain file extension.</param>
		/// <param name="proposeFn">Filename, which is initially proposed when the dialogue is opened.</param>
		/// <returns>Dialogue data including filenames</returns>
		public override AnyUiDialogueDataOpenFile OpenFileFlyoutShow(
			string caption, 
            string message, 
            string proposeFn = null,
            string filter = null)
		{
			var evs = FindEventSession(_bi.SessionId);
            if (evs == null)
                return null;

			var dd = evs.StartModal(new AnyUiDialogueDataOpenFile(
				caption: caption, 
                message: message,
                filter: filter,
                proposeFn: proposeFn));

			// trigger display
			Program.signalNewData(
				new Program.NewDataAvailableArgs(
					Program.DataRedrawMode.None, evs.SessionId));

			// wait modal
			while (!evs.EventDone) Task.Delay(1);
			evs.EventDone = false;

            // dialog result
            if (dd.Result)
                return dd;
			return null;
		}

		/// <summary>
		/// Shows specified dialogue hardware-independent. The technology implementation will show the
		/// dialogue based on the type of provided <c>dialogueData</c>. 
		/// Modal dialogue: this function will block, until user ends dialogue.
		/// </summary>
		/// <param name="dialogueData"></param>
		/// <returns>If the dialogue was end with "OK" or similar success.</returns>
		public override bool StartFlyoverModal(AnyUiDialogueDataBase dialogueData)
        {
			var evs = FindEventSession(_bi.SessionId);
			if (dialogueData == null || evs == null)
				return false;

			var dd = evs.StartModal(dialogueData);

            // trigger display
            Program.signalNewData(
                new Program.NewDataAvailableArgs(
                    Program.DataRedrawMode.None, evs.SessionId));

            // wait modal
            while (!evs.EventDone) Task.Delay(1);
            evs.EventOpen = false;
			evs.EventDone = false;

            // trigger display (again)
            //Program.signalNewData(
            //    new Program.NewDataAvailableArgs(
            //        Program.DataRedrawMode.None, evs.SessionId));
            _jsRuntime.InvokeVoidAsync("blazorCloseModalForce");

            return dd.Result;

			//// access
			//if (dialogueData == null)
			//    return false;

			//// make sure to reset
			//dialogueData.Result = false;

			//AnyUiHtmlEventSession found = null;
			//lock (htmlDotnetLock)
			//{
			//    foreach (var s in sessions)
			//    {
			//        if (_bi.SessionId == s.SessionId)
			//        {
			//            found = s;
			//            break;
			//        }
			//    }
			//}

			//if (found != null)
			//{
			//    found.htmlEventInputs.Clear();
			//    found.htmlEventType = "StartFlyoverModal";
			//    found.htmlEventInputs.Add(dialogueData);

			//    found.htmlEventIn = true;
			//    Program.signalNewData(
			//        new Program.NewDataAvailableArgs(
			//            Program.DataRedrawMode.RebuildTreeKeepOpen, found.SessionId)); // build new tree

			//    while (!found.htmlEventOut) Task.Delay(1);
			//    if (dialogueData is AnyUiDialogueDataTextEditor ddte)
			//    {
			//        if (found.htmlEventOutputs.Count == 2)
			//        {
			//            ddte.Text = (string)found.htmlEventOutputs[0];
			//            ddte.Result = (bool)found.htmlEventOutputs[1];
			//        }
			//    }
			//    if (dialogueData is AnyUiDialogueDataSelectFromList ddsfl)
			//    {
			//        ddsfl.Result = false;
			//        if (found.htmlEventOutputs.Count == 1)
			//        {
			//            int iDdsfl = (int)found.htmlEventOutputs[0];
			//            ddsfl.Result = true;
			//            ddsfl.ResultIndex = iDdsfl;
			//            ddsfl.ResultItem = ddsfl.ListOfItems[iDdsfl];
			//        }
			//    }
			//    found.htmlEventType = "";
			//    found.htmlEventOutputs.Clear();
			//    found.htmlEventOut = false;
			//    found.htmlEventInputs.Clear();
			//    found.htmlDotnetEventIn = false;
			//}
			//// result
			//return dialogueData.Result;
		}

        /// <summary>
        /// Think, this is deprecated
        /// </summary>
        /// <param name="el"></param>
        /// <param name="mode"></param>
        public void UpdateRenderElements(AnyUiUIElement el, AnyUiRenderMode mode)
        {
            //
            // access
            //
            if (el == null)
                return;

            //
            // recurse
            //

            if (el is AnyUi.IEnumerateChildren ien)
                foreach (var elch in ien.GetChildren())
                    UpdateRenderElements(elch, mode: mode);

        }

        /// <summary>
        /// If supported by implementation technology, will set Clipboard (copy/ paste buffer)
        /// of the main application computer.
        /// </summary>
        public override void ClipboardSet(AnyUiClipboardData cb)
        {
            // see: https://www.meziantou.net/copying-text-to-clipboard-in-a-blazor-application.htm

            if (_jsRuntime != null && cb != null)
            {
                _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", cb.Text);
            }
        }
    }
}
