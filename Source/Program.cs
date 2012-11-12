﻿#region Copyright (c) 2006-2011 LOKAD SAS. All rights reserved

// You must not remove this notice, or any other, from this software.
// This document is the property of LOKAD SAS and must not be disclosed

#endregion

using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Lokad.CodeDsl
{
    class Program
    {
        static readonly Mutex AppLock = new Mutex(true, "2DB34E68-F80D-4ED0-975A-409C2CDAF241");

        static readonly ConcurrentDictionary<string, string> States = new ConcurrentDictionary<string, string>();
        public static NotifyIcon TrayIcon;

        static void Main(string[] args)
        {
            if (!AppLock.WaitOne(TimeSpan.Zero, true))
            {
                return;
            }

            var iconStream = Assembly.GetEntryAssembly().GetManifestResourceStream("Lokad.CodeDsl.code_colored.ico");
            TrayIcon = new NotifyIcon
            {
                Visible = true,
            };

            if (iconStream != null)
                TrayIcon.Icon = new Icon(iconStream);

            TrayIcon.Click += TrayIconClick;

            var path = FigureOutLookupPath(args);
            var info = new DirectoryInfo(path);
            var tipText = string.Format("Using lookup path: {0}\r\nClick icon to exit.", info.FullName);
            TrayIcon.ShowBalloonTip(5000, "Dsl started", tipText, ToolTipIcon.Info);

            var files = info.GetFiles("*.ddd", SearchOption.AllDirectories);

            foreach (var fileInfo in files)
            {
                var text = File.ReadAllText(fileInfo.FullName);
                Changed(fileInfo.FullName, text);
                try
                {
                    Rebuild(text, fileInfo.FullName);
                }
                catch (Exception ex)
                {
                    TrayIcon.ShowBalloonTip(10000, "Parse error - " + fileInfo.Name, ex.Message, ToolTipIcon.Error);
                }
            }

            var notifiers = files
                .Select(f => f.DirectoryName)
                .Distinct()
                .Select(d => new FileSystemWatcher(d, "*.ddd"))
                .ToArray();

            foreach (var notifier in notifiers)
            {
                notifier.Changed += NotifierOnChanged;
                notifier.EnableRaisingEvents = true;
            }

            AppDomain.CurrentDomain.ProcessExit += CurrentDomainProcessExit;
            Application.ThreadExit += ApplicationThreadExit;
            Application.Run(new Empty());
        }

        static void ApplicationThreadExit(object sender, EventArgs e)
        {
            Close();
        }

        static void CurrentDomainProcessExit(object sender, EventArgs e)
        {
            Close();
        }

        static void TrayIconClick(object sender, EventArgs e)
        {
            Close();
            Application.Exit();
        }

        private static void Close()
        {
            if (TrayIcon != null)
            {
                TrayIcon.Dispose();
            }
        }

        static string FigureOutLookupPath(string[] args)
        {
            var current = Directory.GetCurrentDirectory();

            if (args.Length > 0)
            {
                return args[0];
            }
            var dir = new DirectoryInfo(current);
            switch (dir.Name)
            {
                case "Release":
                case "Debug":
                    return "../../..";
            }
            return dir.FullName;
        }

        static void NotifierOnChanged(object sender, FileSystemEventArgs args)
        {
            if (!File.Exists(args.FullPath)) return;

            try
            {
                var text = File.ReadAllText(args.FullPath);

                if (!Changed(args.FullPath, text))
                    return;


                var message = string.Format("File {1}-{0}", args.Name, args.ChangeType);
                Console.WriteLine(message);
                Rebuild(text, args.FullPath);

                TrayIcon.ShowBalloonTip(3000, args.Name, "File rebuilded", ToolTipIcon.Info);
                SystemSounds.Beep.Play();
            }
            catch (IOException) {}
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                TrayIcon.ShowBalloonTip(10000, "Error", ex.Message, ToolTipIcon.Error);

                SystemSounds.Exclamation.Play();
            }
        }

        static bool Changed(string path, string value)
        {
            var changed = false;
            States.AddOrUpdate(path, key =>
                {
                    changed = true;
                    return value;
                }, (s, s1) =>
                    {
                        changed = s1 != value;
                        return value;
                    });
            return changed;
        }

        static void Rebuild(string text, string fullPath)
        {
            var dsl = text;
            var generator = new TemplatedGenerator
                {
                    GenerateInterfaceForEntityWithModifiers = "?",
                    TemplateForInterfaceName = "public interface I{0}Aggregate",
                    TemplateForInterfaceMember = "void When({0} c);",
                    ClassNameTemplate = @"
    

[DataContract(Namespace = {1})]
public partial class {0}",
                    MemberTemplate = "[DataMember(Order = {0})] public {1} {2} {{ get; private set; }}",
                    
                };

            File.WriteAllText(Path.ChangeExtension(fullPath, "cs"), GeneratorUtil.Build(dsl, generator));
        }
    }
}