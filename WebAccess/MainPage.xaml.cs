﻿/*
 * WebAccess\MainPage.xaml.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.3.0
 * Source: https://wp8webserver.codeplex.com
 *
 * Finds the WiFi address, displays the URL, and starts the web server.
 */

using System;
using System.Collections.Generic;
using System.Linq;
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
		// Constructor
		public MainPage ()
		{
			InitializeComponent();

			// Sample code to localize the ApplicationBar
			//BuildLocalizedApplicationBar();
		}

		private void PhoneApplicationPage_Loaded (object sender, RoutedEventArgs e)
		{
			foreach (HostName name in NetworkInformation.GetHostNames())
			{
				if (name.IPInformation.NetworkAdapter.IanaInterfaceType == 71)
				{
					ServerUrl.Text = "http://" + name.CanonicalName + ":9999";
					break;
				}
			}
			WebServer server = new WebServer(9999, WebApplication.ServiceRequest);
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