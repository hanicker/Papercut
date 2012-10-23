﻿/*  
 * Papercut
 *
 *  Copyright © 2008 - 2012 Ken Robertson
 *  
 *  Licensed under the Apache License, Version 2.0 (the "License");
 *  you may not use this file except in compliance with the License.
 *  You may obtain a copy of the License at
 *  
 *  http://www.apache.org/licenses/LICENSE-2.0
 *  
 *  Unless required by applicable law or agreed to in writing, software
 *  distributed under the License is distributed on an "AS IS" BASIS,
 *  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *  See the License for the specific language governing permissions and
 *  limitations under the License.
 *  
 */

namespace Papercut
{
	#region Using

	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Diagnostics;
	using System.Diagnostics.CodeAnalysis;
	using System.Drawing;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Forms;
	using System.Windows.Input;
	using System.Windows.Media;
	using System.Windows.Threading;

	using Net.Mail;
	using Net.Mime;

	using Papercut.Properties;
	using Papercut.Smtp;

	using Application = System.Windows.Application;
	using ContextMenu = System.Windows.Forms.ContextMenu;
	using ListBox = System.Windows.Controls.ListBox;
	using MenuItem = System.Windows.Forms.MenuItem;
	using MessageBox = System.Windows.MessageBox;
	using Panel = System.Windows.Controls.Panel;
	using Point = System.Windows.Point;

	#endregion

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		#region Constants and Fields

		/// <summary>
		///   The delete lock object.
		/// </summary>
		private readonly object deleteLockObject = new object();

		/// <summary>
		///   The notification.
		/// </summary>
		private readonly NotifyIcon notification;

		/// <summary>
		///   The server.
		/// </summary>
		private readonly Server server;

	    private Task _currentMessageLoadTask = null;

	    private CancellationTokenSource _currentMessageCancellationTokenSource = null;

		#endregion

		#region Constructors and Destructors

		/// <summary>
		/// Initializes a new instance of the <see cref="MainWindow"/> class. 
		///   Initializes a new instance of the <see cref="MainWindow"/> class. Initializes a new instance of the <see cref="MainWindow"/> class.
		/// </summary>
		public MainWindow()
		{
			this.InitializeComponent();

			// Set up the notification icon
			this.notification = new NotifyIcon();
			this.notification.Icon =
				new Icon(Application.GetResourceStream(new Uri("/Papercut;component/App.ico", UriKind.Relative)).Stream);
			this.notification.Text = "Papercut";
			this.notification.Visible = true;
			this.notification.DoubleClick += delegate
				{
					this.Show();
					this.WindowState = WindowState.Normal;
					this.Topmost = true;
					this.Focus();
					this.Topmost = false;
				};
			this.notification.BalloonTipClicked += delegate
				{
					this.Show();
					this.WindowState = WindowState.Normal;
					this.messagesList.SelectedIndex = this.messagesList.Items.Count - 1;
				};
			this.notification.ContextMenu = new ContextMenu(
				new[]
					{
						new MenuItem(
							"Show", 
							delegate
								{
									this.Show();
									this.WindowState = WindowState.Normal;
									this.Focus();
								}), new MenuItem("Exit", delegate { this.Close(); })
					});

			// Set the version label
			this.versionLabel.Content = string.Format(
				"Papercut v{0}", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));

			// Load existing messages
			this.LoadMessages();
			this.messagesList.Items.SortDescriptions.Add(new SortDescription("ModifiedDate", ListSortDirection.Ascending));

			// Begin listening for new messages
			Processor.MessageReceived += this.Processor_MessageReceived;

			// Start listening for connections
			this.server = new Server();
			try
			{
				this.server.Start();
			}
			catch
			{
				MessageBox.Show(
					"Failed to bind to the address/port specified.  The port may already be in use by another process.  Please change the configuration in the Options dialog.", 
					"Operation Failure");
			}

			this.SetTabs();

			// Minimize if set to
			if (Settings.Default.StartMinimized)
			{
				this.Hide();
			}
		}

		#endregion

		#region Delegates

		/// <summary>
		/// The message notification delegate.
		/// </summary>
		/// <param name="entry">
		/// The entry. 
		/// </param>
		private delegate void MessageNotificationDelegate(MessageEntry entry);

		#endregion

		#region Methods

		/// <summary>
		/// The on state changed.
		/// </summary>
		/// <param name="e">
		/// The e. 
		/// </param>
		protected override void OnStateChanged(EventArgs e)
		{
			// Hide the window if minimized so it doesn't show up on the task bar
			if (this.WindowState == WindowState.Minimized)
			{
				this.Hide();
			}

			base.OnStateChanged(e);
		}

		/// <summary>
		/// The get object data from point.
		/// </summary>
		/// <param name="source">
		/// The source. 
		/// </param>
		/// <param name="point">
		/// The point. 
		/// </param>
		/// <returns>
		/// The get object data from point. 
		/// </returns>
		private static string GetObjectDataFromPoint(ListBox source, Point point)
		{
			var element = source.InputHitTest(point) as UIElement;
			if (element != null)
			{
				// Get the object from the element
				object data = DependencyProperty.UnsetValue;
				while (data == DependencyProperty.UnsetValue)
				{
					// Try to get the object value for the corresponding element
					data = source.ItemContainerGenerator.ItemFromContainer(element);

					// Get the parent and we will iterate again
					if (data == DependencyProperty.UnsetValue)
					{
						element = VisualTreeHelper.GetParent(element) as UIElement;
					}

					// If we reach the actual listbox then we must break to avoid an infinite loop
					if (element == source)
					{
						return null;
					}
				}

				// Return the data that we fetched only if it is not Unset value, 
				// which would mean that we did not find the data
				if (data is MessageEntry)
				{
					return ((MessageEntry)data).File;
				}
			}

			return null;
		}

		/// <summary>
		/// Add a newly received message and show the balloon notification
		/// </summary>
		/// <param name="entry">
		/// The entry. 
		/// </param>
		private void AddNewMessage(MessageEntry entry)
		{
			// Add it to the list box
			this.messagesList.Items.Add(entry);

			// Show the notification
			this.notification.ShowBalloonTip(5000, string.Empty, "New message received!", ToolTipIcon.Info);
		}

		/// <summary>
		/// The exit_ click.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void Exit_Click(object sender, RoutedEventArgs e)
		{
			this.Close();
		}

		/// <summary>
		/// The go to site.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void GoToSite(object sender, MouseButtonEventArgs e)
		{
			Process.Start("http://papercut.codeplex.com/");
		}

		/// <summary>
		/// Load existing messages from the file system
		/// </summary>
		private void LoadMessages()
		{
			string[] files = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.eml");

			foreach (var entry in files.Select(file => new MessageEntry(file)))
			{
				this.messagesList.Items.Add(entry);
			}
		}

		/// <summary>
		/// The mouse down handler.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void MouseDownHandler(object sender, MouseButtonEventArgs e)
		{
			return;

			/*
ListBox parent = (ListBox)sender;

// Get the object source for the selected item
string data = GetObjectDataFromPoint(parent, e.GetPosition(parent));

// If the data is not null then start the drag drop operation
if (data != null)
{
	DataObject doo = new DataObject(DataFormats.FileDrop, new[] { data });
	DragDrop.DoDragDrop(parent, doo, DragDropEffects.Copy);
}
			*/
		}

		/// <summary>
		/// The options_ click.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void Options_Click(object sender, RoutedEventArgs e)
		{
			var ow = new OptionsWindow();
			ow.Owner = this;
			ow.ShowInTaskbar = false;

			if (ow.ShowDialog().Value)
			{
				try
				{
					// Force the server to rebind
					this.server.Bind();

					this.SetTabs();
				}
				catch (Exception ex)
				{
					MessageBox.Show(
						"Failed to rebind to the address/port specified.  The port may already be in use by another process.  Please update the configuration.", 
						"Operation Failure");
					this.Options_Click(null, null);
				}
			}
		}

		/// <summary>
		/// The processor_ message received.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void Processor_MessageReceived(object sender, MessageEventArgs e)
		{
			// This takes place on a background thread from the SMTP server
			// Dispatch it back to the main thread for the update
			this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new MessageNotificationDelegate(this.AddNewMessage), e.Entry);
		}

		/// <summary>
		/// Write the HTML to a temporary file and render it to the HTML view
		/// </summary>
		/// <param name="mailMessageEx">
		/// The mail Message Ex. 
		/// </param>
		private void SetBrowserDocument(MailMessageEx mailMessageEx)
		{
			const int Length = 256;

			// double d = new Random().NextDouble();
			string tempPath = Path.GetTempPath();
			string htmlFile = Path.Combine(tempPath, "papercut.htm");

			string htmlText = mailMessageEx.Body;

			foreach (var attachment in mailMessageEx.Attachments)
			{
				if ((!string.IsNullOrEmpty(attachment.ContentId)) && (attachment.ContentStream != null))
				{
					string fileName = Path.Combine(tempPath, attachment.ContentId);

					using (var fs = File.OpenWrite(fileName))
					{
						var buffer = new byte[Length];
						int bytesRead = attachment.ContentStream.Read(buffer, 0, Length);

						// write the required bytes
						while (bytesRead > 0)
						{
							fs.Write(buffer, 0, bytesRead);
							bytesRead = attachment.ContentStream.Read(buffer, 0, Length);
						}

						fs.Close();
					}

					htmlText =
						htmlText.Replace(string.Format("cid:{0}", attachment.ContentId), attachment.ContentId).Replace(
							string.Format("cid:'{0}'", attachment.ContentId), attachment.ContentId).Replace(
								string.Format("cid:\"{0}\"", attachment.ContentId), attachment.ContentId);
				}
			}

			using (TextWriter f = new StreamWriter(htmlFile)) f.Write(htmlText);

			this.htmlView.Navigate(new Uri(htmlFile));
			this.htmlView.Refresh();

			this.defaultHtmlView.Navigate(new Uri(htmlFile));
			this.defaultHtmlView.Refresh();
		}

		/// <summary>
		/// The set tabs.
		/// </summary>
		private void SetTabs()
		{
			if (Settings.Default.ShowDefaultTab)
			{
				this.tabControl.SelectedIndex = 0;
				this.defaultTab.Visibility = Visibility.Visible;
			}
			else
			{
				this.tabControl.SelectedIndex = 1;
				this.defaultTab.Visibility = Visibility.Collapsed;
			}
		}

		/// <summary>
		/// The window_ closing.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void Window_Closing(object sender, CancelEventArgs e)
		{
			this.notification.Dispose();
			this.server.Stop();
		}

		/// <summary>
		/// The delete button_ click.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void deleteButton_Click(object sender, RoutedEventArgs e)
		{
			// Lock to prevent rapid clicking issues
			lock (this.deleteLockObject)
			{
				Array messages = new MessageEntry[this.messagesList.SelectedItems.Count];
				this.messagesList.SelectedItems.CopyTo(messages, 0);

				// Capture index position first
				int index = this.messagesList.SelectedIndex;

				foreach (MessageEntry entry in messages)
				{
					// Delete the file and remove the entry
					if (File.Exists(entry.File))
					{
						File.Delete(entry.File);
					}

					this.messagesList.Items.Remove(entry);
				}

				// If there are more than the index location, keep the same position in the list
				if (this.messagesList.Items.Count > index)
				{
					this.messagesList.SelectedIndex = index;
				}
					
					
					// If there are fewer, move to the last one
				else if (this.messagesList.Items.Count > 0)
				{
					this.messagesList.SelectedIndex = this.messagesList.Items.Count - 1;
				}
				else if (this.messagesList.Items.Count == 0)
				{
					tabControl.IsEnabled = false;
				}
			}
		}

		/// <summary>
		/// The forward button_ click.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		private void forwardButton_Click(object sender, RoutedEventArgs e)
		{
			var entry = this.messagesList.SelectedItem as MessageEntry;
			var fw = new ForwardWindow(entry.File);
			fw.Owner = this;
			fw.ShowDialog();
		}

		/// <summary>
		/// The messages list_ selection changed.
		/// </summary>
		/// <param name="sender">
		/// The sender. 
		/// </param>
		/// <param name="e">
		/// The e. 
		/// </param>
		[SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1126:PrefixCallsCorrectly", Justification = "Reviewed. Suppression is OK here.")]
		private void messagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			// If there are no selected items, then disable the Delete button, clear the boxes, and return
			if (e.AddedItems.Count == 0)
			{
				this.deleteButton.IsEnabled = false;
				this.forwardButton.IsEnabled = false;
				this.rawView.Text = string.Empty;
				this.bodyView.Text = string.Empty;
				this.htmlViewTab.Visibility = Visibility.Hidden;
				this.tabControl.SelectedIndex = this.defaultTab.IsVisible ? 0 : 1;

				return;
			}

		    var setTitle = new Action<string>
		        ((t) =>
		            {
		                Subject.Content = t;
		                Subject.ToolTip = t;
		            });

			try
			{
				tabControl.IsEnabled = false;
                SpinAnimation.Visibility = Visibility.Visible;
			    setTitle("Loading...");

                if (_currentMessageLoadTask != null && _currentMessageLoadTask.Status == TaskStatus.Running && _currentMessageCancellationTokenSource != null)
                {
                    _currentMessageCancellationTokenSource.Cancel();
                }

                _currentMessageCancellationTokenSource = new CancellationTokenSource();

			    _currentMessageLoadTask = Task.Factory.StartNew
			        (() =>
			            {
			                // Load the file as an array of lines
			                var lines = new List<string>();
			                using (var sr = new StreamReader(((MessageEntry)e.AddedItems[0]).File))
			                {
			                    string line;
			                    while ((line = sr.ReadLine()) != null)
			                    {
			                        lines.Add(line);
			                    }
			                }

			                return lines.ToArray();

			            },
			         _currentMessageCancellationTokenSource.Token).ContinueWith
			        ((task) =>
			            {
			                // Load the MIME body
			                var mimeReader = new MimeReader(task.Result);
			                MimeEntity me = mimeReader.CreateMimeEntity();

			                return Tuple.Create(task.Result, me.ToMailMessageEx());

			            },
			         _currentMessageCancellationTokenSource.Token,
			         TaskContinuationOptions.NotOnCanceled,
			         TaskScheduler.Default).ContinueWith
			        ((task) =>
			            {
                            if (task.IsCanceled)
                            {
                                return;
                            }

			                var resultTuple = task.Result;

			                var mme = resultTuple.Item2;

			                // set the raw view...
			                this.rawView.Text = string.Join("\n", resultTuple.Item1);

			                this.bodyView.Text = mme.Body;
			                this.bodyViewTab.Visibility = Visibility.Visible;

			                this.defaultBodyView.Text = mme.Body;

			                this.FromEdit.Text = mme.From.ToString();
			                this.ToEdit.Text = mme.To.ToString();
			                this.DateEdit.Text = mme.DeliveryDate.ToString();
			                this.SubjectEdit.Text = mme.Subject;

                            setTitle(mme.Subject);

			                // If it is HTML, render it to the HTML view
			                if (mme.IsBodyHtml)
			                {
			                    this.SetBrowserDocument(mme);
			                    this.htmlViewTab.Visibility = Visibility.Visible;

			                    this.defaultHtmlView.Visibility = Visibility.Visible;
			                    this.defaultBodyView.Visibility = Visibility.Collapsed;
			                }
			                else
			                {
			                    this.htmlViewTab.Visibility = Visibility.Hidden;
			                    if (this.defaultTab.IsVisible)
			                    {
			                        this.tabControl.SelectedIndex = 0;
			                    }
			                    else if (Equals(this.tabControl.SelectedItem, this.htmlViewTab))
			                    {
			                        this.tabControl.SelectedIndex = 2;
			                    }

			                    this.defaultHtmlView.Visibility = Visibility.Collapsed;
			                    this.defaultBodyView.Visibility = Visibility.Visible;
			                }

			                SpinAnimation.Visibility = Visibility.Collapsed;
			                tabControl.IsEnabled = true;

			                // Enable the delete and forward button
			                this.deleteButton.IsEnabled = true;
			                this.forwardButton.IsEnabled = true;
			            },
			         _currentMessageCancellationTokenSource.Token,
			         TaskContinuationOptions.NotOnCanceled,
			         TaskScheduler.FromCurrentSynchronizationContext());
			}
			catch
			{
				this.bodyViewTab.Visibility = Visibility.Hidden;
				this.htmlViewTab.Visibility = Visibility.Hidden;
                setTitle("Papercut");
				this.tabControl.SelectedIndex = 1;
			}
		}

		#endregion
	}
}