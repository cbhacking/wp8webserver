/*
 * WebAccess\MainPage.xaml.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.4.9
 * Source: https://wp8webserver.codeplex.com
 *
 * Finds the WiFi address, displays the URL, and starts the web server.
 */

using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using WebAccess.Resources;
using Windows.Networking;
using Windows.Networking.Connectivity;

using HttpServer;

namespace WebAccess
{
	public partial class MainPage : PhoneApplicationPage
	{
		static IsolatedStorageSettings settings = IsolatedStorageSettings.ApplicationSettings;
		static WebServer server = null;
		static ushort port;

		// Constructor
		public MainPage ()
		{
			InitializeComponent();

			// Sample code to localize the ApplicationBar
			//BuildLocalizedApplicationBar();
		}

		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			base.OnNavigatedTo(e);
			ServerUrl.Text = "Server is starting...";
			GetPort();
			StartServer();
		}

		private void UpdatePort ()
		{
			if (ushort.TryParse(PortText.Text, out port))
			{
				settings["port"] = port;
			}
			else
			{
				MessageBox.Show("Port must be a number less than " + ushort.MaxValue + "!");
				GetPort();
			}
		}

		private void GetPort ()
		{
			if (!(settings.Contains("port") &&
				ushort.TryParse(settings["port"].ToString(), out port)))
			{
				// Parsing the port failed!
				port = 9999;
				settings["port"] = port;
			}
			PortText.Text = port.ToString();
		}

		private void StartServer ()
		{
			try
			{
				if (null != server)
				{
					server.Close();
					server = null;
					GC.Collect();
				}
				foreach (HostName name in NetworkInformation.GetHostNames())
				{
					if (name.IPInformation.NetworkAdapter.IanaInterfaceType == 71)
					{
						ServerUrl.Text = "http://" + name.CanonicalName + ":" + port;
						break;
					}
				}
				server = new WebServer(port, WebApplication.ServiceRequest);
			}
			catch (Exception ex)
			{
				MessageBox.Show("Unable to start HTTP listener!\nException: " + ex.ToString());
				Application.Current.Terminate();
			}
		}

		private void RestartButton_Click (object sender, RoutedEventArgs e)
		{
			ServerUrl.Text = "Restarting server...";
			UpdatePort();
			StartServer();
		}

		// Sample code for building a localized ApplicationBar
		//private void BuildLocalizedApplicationBar()
		//{
		//    // Set the page's ApplicationBar to a new instance of ApplicationBar.
		//    ApplicationBar = new ApplicationBar();

		//    // Create a new button and set the text value to the localized string from AppResources.
		//    ApplicationBarIconButton appBarButton = new ApplicationBarIconButton(new Uri("/Assets/AppBar/appbar.add.rest.png", UriKind.Relative));
		//    appBarButton.Text = AppResources.AppBarButtonText;
		//    ApplicationBar.Buttons.Add(appBarButton);

		//    // Create a new menu item with the localized string from AppResources.
		//    ApplicationBarMenuItem appBarMenuItem = new ApplicationBarMenuItem(AppResources.AppBarMenuItemText);
		//    ApplicationBar.MenuItems.Add(appBarMenuItem);
		//}
	}
}