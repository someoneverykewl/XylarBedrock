using System;
using System.Windows;
using XylarBedrock.Handlers;

namespace XylarBedrock
{
    public partial class App : Application
    {
        public static string Version => "0.0.0.5";
        public static string DisplayName => $"XylarBedrock v{Version}";

        public App() : base()
        {
            DispatcherUnhandledException += RuntimeHandler.OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += RuntimeHandler.OnCurrentDomainUnhandledException;
        }
    }
}
