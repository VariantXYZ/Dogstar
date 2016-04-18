﻿using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MahApps.Metro.Controls.Dialogs;
using DogStar.Resources;
using DogStar.Properties;

using static MahApps.Metro.ThemeManager;
using static DogStar.Helper;
using static DogStar.External;

namespace DogStar
{
	// TODO: Prepatch https://social.msdn.microsoft.com/Forums/vstudio/en-US/9daab290-cf3a-4777-b046-3dc156b184c0/how-to-make-a-wpf-child-window-follow-its-parent?forum=wpf
	// TODO: Make sure to keep patch lists, version.ver, version_precede.ver, management_beta, precede in documents\sega\phantasystaronline2
	// TODO: figure out documents\sega\pso2\download patch lists
	// TODO: Check for pre-patches (see official pso2 launcher)
	// TODO: Implement the functionality behind the toggles on the enhancements menu
	// TODO: hosts.ics check
	// TODO: When configuring PSO2 Proxy and plugin not installed, install plugin on success
	// TODO: Write version.ver to PSO2 Tweaker registry
	// TODO: Figure out why vanilla launcher keeps deleting version.ver. _version.ver?

	public partial class MainWindow : IDisposable
	{
		private readonly DownloadManager _generalDownloadManager = new DownloadManager();

		private CancellationTokenSource _checkCancelSource = new CancellationTokenSource();
		private bool _isCheckPaused;

		public string WindowTittle => $"{ApplicationInfo.Name} {ApplicationInfo.Version}";

		public MainWindow()
		{
			ChangeAppStyle(Application.Current, GetAccent(Settings.Default.AccentColor), GetAppTheme(Settings.Default.Theme));
			InitializeComponent();

			_generalDownloadManager.DownloadStarted += DownloadStarted;
			_generalDownloadManager.DownloadProgressChanged += DownloadProgressChanged;

			Topmost = Settings.Default.AlwaysOnTop;
			Colors.SelectedIndex = Array.IndexOf(UiResources.GetColor().Values.ToArray(), Settings.Default.AccentColor);
			Themes.SelectedIndex = Array.IndexOf(UiResources.GetTheme().Values.ToArray(), Settings.Default.Theme);
		}

		#region Events

		private void PauseCheckButton_Click(object sender, RoutedEventArgs e) => _isCheckPaused = !_isCheckPaused;

		private void Debug_MouseDown(object sender, MouseButtonEventArgs e) => DebugFlyout.IsOpen = !DebugFlyout.IsOpen;

		private void Donate_MouseDown(object sender, MouseButtonEventArgs e) => DonationFlyout.IsOpen = !DebugFlyout.IsOpen;

		private void Twitter_MouseDown(object sender, MouseButtonEventArgs e) => Process.Start(Properties.Resources.DogstarTwitter);

		private void Github_MouseDown(object sender, MouseButtonEventArgs e) => Process.Start(Properties.Resources.DogstarGithub);

		private void Information_MouseDown(object sender, MouseButtonEventArgs e) => Process.Start(Properties.Resources.DogstarSupport);

		private void CancelCheckButton_Click(object sender, RoutedEventArgs e) => _checkCancelSource.Cancel();

		private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e) => Topmost = Settings.Default.AlwaysOnTop = AlwaysOnTop.IsChecked.GetValueOrDefault();

		private void Launch_Changed(object sender, RoutedEventArgs e) => Settings.Default.CloseOnLaunch = Launch.IsChecked.GetValueOrDefault();

		private void DonateToDogstar_Click(object sender, RoutedEventArgs e) => Process.Start(Properties.Resources.DogstarDonation);

		private void DonateToPolaris_Click(object sender, RoutedEventArgs e) => Process.Start(Properties.Resources.PolarisDonation);

		private async void CheckButton_Click(object sender, RoutedEventArgs e) => await CheckGameFiles(UpdateMethod.FileCheck);

		private void EnhancementsTile_Click(object sender, RoutedEventArgs e) => EnhancementsTabItem.IsSelected = true;

		private void EnhancementsBackButton_Click(object sender, RoutedEventArgs e) => MainTabItem.IsSelected = true;

		private void TileCopy1_Click(object sender, RoutedEventArgs e) => OtherTabItem.IsSelected = true;

		private async void OtherProxyConfig_Click(object sender, RoutedEventArgs e) => await ConfigProxy();


		private async void metroWindow_Loaded(object sender, RoutedEventArgs e)
		{
			var sdfasdf = new PrecedeWindow { Owner = this };
			sdfasdf.Show();

			if (!Settings.Default.IsGameInstalled)
			{
				var gamefolder = GetTweakerGameFolder();

				if (string.IsNullOrWhiteSpace(gamefolder))
				{
					await SetupGameInfo();
				}
				else
				{
					var result = await this.ShowMessageAsync(Text.GameDetected, $"\"{gamefolder}\"", AffirmNeg, YesNo);

					if (result == MessageDialogResult.Affirmative)
					{
						Settings.Default.GameFolder = gamefolder;
						Settings.Default.IsGameInstalled = true;
					}
					else
					{
						await SetupGameInfo();
					}
				}
			}

			if (Settings.Default.IsGameInstalled)
			{
				await Task.Run(() => CreateDirectoryIfNoneExists(GameConfigFolder));

				if (!Directory.Exists(Settings.Default.GameFolder))
				{
					var result = await this.ShowMessageAsync(Text.MissingFolder, Text.MovedOrDeleted, AffirmNeg, MovedDeleted);

					if (result == MessageDialogResult.Affirmative)
					{
						await SelectGameFolder();
					}
					else
					{
						Settings.Default.IsGameInstalled = false;
						Settings.Default.GameFolder = string.Empty;
						await SetupGameInfo();
					}
				}
			}

			Settings.Default.Save();

			string editionPath = Path.Combine(Settings.Default.GameFolder, "edition.txt");

			if (File.Exists(editionPath))
			{
				string edition = await Task.Run(() => File.ReadAllText(editionPath));

				if (edition != "jp")
				{
					await this.ShowMessageAsync(Text.Warning, Text.NonJPPSO2);
				}
			}

			if (!await IsGameUpToDate())
			{
				var result = await this.ShowMessageAsync(Text.GameUpdate, Text.GameUpdateAvailable, MessageDialogStyle.AffirmativeAndNegative, YesNo);

				if (result == MessageDialogResult.Affirmative)
				{
					await CheckGameFiles(UpdateMethod.Update);
				}
			}
			else
			{
				EnglishPatchToggle.IsChecked = Settings.Default.InstalledEnglishPatch != 0;
				LargeFilesToggle.IsChecked = Settings.Default.InstalledLargeFiles != 0;

				if (EnglishPatchToggle.IsChecked == true || LargeFilesToggle.IsChecked == true)
				{
					var files = (JArray)(await GetArghlexJson()).files;
					dynamic entryEn = files.Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).StartsWith("patch_"));
					dynamic entryLarge = files.Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).EndsWith("_largefiles.rar"));

					if (entryEn != null && await CheckEnglishPatchVersion((long)entryEn.modtime))
					{
						GeneralDownloadTab.IsSelected = true;
						await DownloadEnglishPatch((string)entryEn.name, (int)entryEn.size, (long)entryEn.modtime);
						MainTabItem.IsSelected = true;
					}
					if (entryLarge != null && await CheckLargeFilesVersion((long)entryLarge.modtime))
					{
						GeneralDownloadTab.IsSelected = true;
						await DownloadLargeFiles((string)entryLarge.name, (int)entryLarge.size, (long)entryLarge.modtime);
						MainTabItem.IsSelected = true;
					}
				}
			}

		}

		private void DownloadStarted(object sender, string e)
		{
			CurrentGeneralDownloadActionlabel.Dispatcher.InvokeAsync(() =>
			{
				GeneralDownloadProgressbar.Maximum = 100;
				GeneralDownloadProgressbar.IsIndeterminate = false;
				CurrentGeneralDownloadSizeActionLable.Visibility = Visibility.Visible;
				CurrentGeneralDownloadActionlabel.Content = Path.GetFileNameWithoutExtension(e);
			});
		}

		private void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
		{
			CheckDownloadProgressbar.Dispatcher.InvokeAsync(() =>
			{
				GeneralDownloadProgressbar.Value = e.ProgressPercentage;
				CurrentGeneralDownloadSizeActionLable.Content = $"{SizeSuffix(e.BytesReceived)}/{SizeSuffix(e.TotalBytesToReceive)}";
			});
		}

		private async void LaunchButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				using (var client = AquaClient)
				{
					string management = await client.DownloadStringTaskAsync(ManagementUrl);
					if (management.Contains("IsInMaintenance=1"))
					{
						var result = await this.ShowMessageAsync(Text.ServerMaintenance, Text.GameIsDown, AffirmNeg, YesNo);
						if (result != MessageDialogResult.Affirmative)
							return;
					}
				}

				if (await Task.Run((Func<bool>)LaunchGame) && Settings.Default.CloseOnLaunch)
				{
					Close();
				}
			}
			catch (Exception ex)
			{
				await this.ShowMessageAsync(string.Empty, ex.Message);
			}
		}

		private void Color_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				Settings.Default.AccentColor = Colors.SelectedValue.ToString();
				ChangeAppStyle(Application.Current, GetAccent(Settings.Default.AccentColor), GetAppTheme(Settings.Default.Theme));
				Settings.Default.Save();
			}
		}

		private void Theme_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				Settings.Default.Theme = Themes.SelectedValue.ToString();
				ChangeAppStyle(Application.Current, GetAccent(Settings.Default.AccentColor), GetAppTheme(Settings.Default.Theme));
				Settings.Default.Save();
			}
		}

		private async void EnglishPatchToggle_Checked(object sender, RoutedEventArgs e)
		{
			GeneralDownloadTab.IsSelected = true;
			dynamic jsonData = await GetArghlexJson();
			dynamic entry = ((JArray)jsonData.files).Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).StartsWith("patch_"));
			if (entry != null)
				await DownloadEnglishPatch((string)entry.name, (int)entry.size, (int)entry.modtime);
			EnhancementsTabItem.IsSelected = true;
		}

		private async void LargeFilesToggle_Checked(object sender, RoutedEventArgs e)
		{
			GeneralDownloadTab.IsSelected = true;
			dynamic jsonData = await GetArghlexJson();
			dynamic entry = ((JArray)jsonData.files).Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).EndsWith("_largefiles.rar"));
			if (entry != null)
				await DownloadLargeFiles((string)entry.name, (int)entry.size, (int)entry.modtime);
			EnhancementsTabItem.IsSelected = true;
		}

		private void StoryPatchToggle_Checked(object sender, RoutedEventArgs e)
		{

		}

		private async void Pso2ProxyToggle_Checked(object sender, RoutedEventArgs e)
		{
			if (await Task.Run((Func<bool>)ProxyCheck))
			{
				// TODO: Put dll in plugins
			}
			else
			{
				Pso2ProxyToggle.IsChecked = await ConfigProxy();
			}
		}

		private async void Pso2ProxyToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			// TODO: Remove DLL
			await Task.Run(() => File.WriteAllLines(HostsPath, StripProxyEntries(File.ReadAllLines(HostsPath))));
		}

		private async void EnglishPatchToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			await Task.Run(() => RestorePatchBackup("EnglishPatch"));
			Settings.Default.InstalledEnglishPatch = 0;
		}

		private async void LargeFilesToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			await Task.Run(() => RestorePatchBackup("LargeFiles"));
			Settings.Default.InstalledLargeFiles = 0;
		}

		#endregion

		private async Task CheckGameFiles(UpdateMethod method)
		{
			_checkCancelSource = new CancellationTokenSource();
			_isCheckPaused = false;
			CompletedCheckDownloadActionslabel.Content = string.Empty;
			CompletedCheckActionslabel.Content = string.Empty;
			CurrentCheckActionlabel.Content = string.Empty;
			CurrentCheckDownloadActionlabel.Content = string.Empty;
			CurrentCheckSizeActionLable.Content = string.Empty;
			CheckDownloadProgressbar.Value = 0;
			CheckProgressbar.Value = 0;
			FileCheckTabItem.IsSelected = true;
			var numberDownloaded = 0;
			var numberToDownload = 3;
			var fileOperations = new List<Task>();

			using (var downloadManager = new DownloadManager())
			{
				downloadManager.DownloadStarted += (s, e) =>
				{
					CurrentCheckDownloadActionlabel.Dispatcher.InvokeAsync(() =>
					{
						CheckDownloadProgressbar.Maximum = 100;
						CurrentCheckDownloadActionlabel.Content = Path.GetFileNameWithoutExtension(e);
					});
				};

				downloadManager.DownloadProgressChanged += (s, e) =>
				{
					CheckDownloadProgressbar.Dispatcher.InvokeAsync(() =>
					{
						CheckDownloadProgressbar.Value = e.ProgressPercentage;
						CurrentCheckSizeActionLable.Content = $"{SizeSuffix(e.BytesReceived)}/{SizeSuffix(e.TotalBytesToReceive)}";
					});
				};

				downloadManager.DownloadCompleted += (s, e) =>
				{
					CheckDownloadProgressbar.Dispatcher.InvokeAsync(() =>
					{
						numberDownloaded++;
						CompletedCheckDownloadActionslabel.Content = string.Format(Text.DownloadedOf, numberDownloaded, numberToDownload);
					});
				};

				Func<Task> pause = async () =>
				{
					var taskSource = new TaskCompletionSource<object>();
					RoutedEventHandler unpause = (s, e) => taskSource.TrySetResult(null);
					RoutedEventHandler cancel = (s, e) => taskSource.TrySetCanceled();

					try
					{
						PauseCheckButton.Click += unpause;
						CancelCheckButton.Click += cancel;
						downloadManager.PauseDownloads(taskSource.Task);
						await taskSource.Task;
					}
					finally
					{
						PauseCheckButton.Click -= unpause;
						CancelCheckButton.Click -= cancel;
					}
				};

				try
				{
					await Task.Run(() => CreateDirectoryIfNoneExists(GameConfigFolder));
					await Task.Run(() => CreateDirectoryIfNoneExists(DataFolder));

					var launcherlist = await downloadManager.DownloadStringTaskAsync(LauncherListUrl);
					var newlist = await downloadManager.DownloadStringTaskAsync(PatchListUrl);
					var oldlist = await downloadManager.DownloadStringTaskAsync(PatchListOldUrl);

					var launcherlistdata = ParsePatchList(launcherlist).ToArray();
					var newlistdata = ParsePatchList(newlist).ToArray();
					var oldlistdata = ParsePatchList(oldlist).ToArray();

					// TODO: Precede should download in the background. It doesn't affect the game immedately, so the game can be played. How do?
					if (method != UpdateMethod.Precede)
					{
						await RestoreAllPatchBackups();
					}

					if (method == UpdateMethod.Update && Directory.Exists(GameConfigFolder))
					{
						var entryComparer = new PatchListEntryComparer();

						if (File.Exists(LauncherListPath))
						{
							var storedLauncherlist = await Task.Run(() => ParsePatchList(File.ReadAllText(LauncherListPath)));
							launcherlistdata = launcherlistdata.Except(storedLauncherlist, entryComparer).ToArray();
						}

						if (File.Exists(PatchListPath))
						{
							var storedNewlist = await Task.Run(() => ParsePatchList(File.ReadAllText(PatchListPath)));
							newlistdata = newlistdata.Except(storedNewlist, entryComparer).ToArray();
						}

						if (File.Exists(PatchListOldPath))
						{
							var storedOldlist = await Task.Run(() => ParsePatchList(File.ReadAllText(PatchListOldPath)));
							oldlistdata = oldlistdata.Except(storedOldlist, entryComparer).ToArray();
						}
					}

					var lists = launcherlistdata.Concat(newlistdata.Concat(oldlistdata)).ToArray();
					var groups = (from v in lists group v by v.Name into d select d.First()).ToArray();

					CheckProgressbar.Maximum = groups.Length;

					for (var index = 0; index < groups.Length;)
					{
						_checkCancelSource.Token.ThrowIfCancellationRequested();

						if (_isCheckPaused)
						{
							await pause();
						}
						else
						{
							var data = groups[index];
							CurrentCheckActionlabel.Content = Path.GetFileNameWithoutExtension(data.Name);
							var filePath = MakeLocalToGame(Path.ChangeExtension(data.Name, null));

							var upToDate = await Task.Run(() => IsFileUpToDate(filePath, data.Size, data.Hash));
							CheckProgressbar.Value = ++index;
							CompletedCheckActionslabel.Content = string.Format(Text.CheckedOf, index, groups.Length);

							if (!upToDate)
							{
								var patPath = MakeLocalToGame(data.Name);

								fileOperations.Add(downloadManager.DownloadFileTaskAsync(newlistdata.Contains(data) || launcherlistdata.Contains(data) ? new Uri(BasePatch, data.Name) : new Uri(BasePatchOld, data.Name), patPath).ContinueWith(x => MoveAndOverwriteFile(patPath, filePath)));

								numberToDownload++;
								CompletedCheckDownloadActionslabel.Content = string.Format(Text.DownloadedOf, numberDownloaded, numberToDownload);
							}
						}
					}

					await Task.Run(() =>
					{
						File.WriteAllText(LauncherListPath, launcherlist);
						File.WriteAllText(PatchListPath, newlist);
						File.WriteAllText(PatchListOldPath, oldlist);
					});

					numberToDownload++;
					fileOperations.Add(downloadManager.DownloadFileTaskAsync(VersionUrl, VersionPath));

					var downloads = Task.WhenAll(fileOperations);

					while (!downloads.IsCompleted && !downloads.IsCanceled && !downloads.IsFaulted)
					{
						_checkCancelSource.Token.ThrowIfCancellationRequested();

						if (_isCheckPaused)
						{
							await pause();
						}
						else
						{
							await Task.Delay(16);
						}
					}

					await downloads;
				}
				catch when (_checkCancelSource.IsCancellationRequested)
				{
					downloadManager.CancelDownloads();
				}
			}

			if (GameTabItem.IsSelected)
			{
				FlashWindow(this, true);
				if (numberDownloaded > 0)
				{
					await this.ShowMessageAsync(Text.Updated, string.Format(Text.FilesDownloaded, numberDownloaded));
				}
				else
				{
					await this.ShowMessageAsync(Text.Complete, Text.AllFilesValid);
				}
			}

			MainTabItem.IsSelected = true;
		}

		public async Task<bool> ConfigProxy()
		{
			var url = await this.ShowInputAsync(Text.EnterProxy, string.Empty);
			if (!string.IsNullOrWhiteSpace(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute))
			{
				return await SetupProxy(url);
			}

			await this.ShowMessageAsync(Text.InvalidProxyURL, string.Empty);

			return false;
		}

		public async Task<bool> SetupProxy(string url)
		{
			using (var client = AquaClient)
			{
				var json = await client.DownloadStringTaskAsync(url);
				dynamic jsonData = JsonConvert.DeserializeObject(json);

				if (jsonData.version != 1)
				{
					await this.ShowMessageAsync(string.Empty, Text.BadProxyVersion);
					return false;
				}

				var hostAddress = (await Dns.GetHostAddressesAsync((string)jsonData.host))?.FirstOrDefault()?.ToString();
				var file = StripProxyEntries(File.ReadAllLines(HostsPath)).ToArray();
				var lines = file.Concat(new[]
				{
					"# Dogstar Proxy Start", $"{hostAddress} gs001.pso2gs.net # {jsonData.name} Ship 01", $"{hostAddress} gs016.pso2gs.net # {jsonData.name} Ship 02", $"{hostAddress} gs031.pso2gs.net # {jsonData.name} Ship 03", $"{hostAddress} gs046.pso2gs.net # {jsonData.name} Ship 04", $"{hostAddress} gs061.pso2gs.net # {jsonData.name} Ship 05", $"{hostAddress} gs076.pso2gs.net # {jsonData.name} Ship 06", $"{hostAddress} gs091.pso2gs.net # {jsonData.name} Ship 07", $"{hostAddress} gs106.pso2gs.net # {jsonData.name} Ship 08", $"{hostAddress} gs121.pso2gs.net # {jsonData.name} Ship 09", $"{hostAddress} gs136.pso2gs.net # {jsonData.name} Ship 10", "# Dogstar Proxy End"
				});

				File.WriteAllLines(HostsPath, lines);

				client.DownloadFileAsync(new Uri((string)jsonData.publickeyurl), Path.Combine(Settings.Default.GameFolder, "publickey.blob"));

				var gameHost = (await Dns.GetHostAddressesAsync("gs001.pso2gs.net"))?.FirstOrDefault()?.ToString();

				if (gameHost != hostAddress)
				{
					await this.ShowMessageAsync(Text.Failed, Text.ProxyRevert);
					File.WriteAllLines(HostsPath, file);
					return false;
				}

				return true;
			}
		}

		private async Task<bool> InstallGame(string path)
		{
			try
			{
				CreateDirectoryIfNoneExists(path);
				Settings.Default.GameFolder = path;
				Settings.Default.IsGameInstalled = true;
				await CheckGameFiles(UpdateMethod.FileCheck);
				Settings.Default.Save();
				return true;
			}
			catch (Exception)
			{
				Settings.Default.GameFolder = string.Empty;
				Settings.Default.IsGameInstalled = false;
				Settings.Default.Save();
				return false;
			}
		}

		private async Task SetupGameInfo()
		{
			var result = await this.ShowMessageAsync(Text.HaveInstalled, string.Empty, AffirmNeg, YesNo);

			if (result != MessageDialogResult.Affirmative)
			{
				result = await this.ShowMessageAsync(Text.WouldYoulikeToInstall, string.Empty, AffirmNeg, YesNo);

				if (result == MessageDialogResult.Affirmative)
				{
					var path = await this.ShowFolderSelectAsync(string.Empty, Text.SelectInstallLocation, Properties.Resources.DefaultInstallDir);

					while (!string.IsNullOrWhiteSpace(path) && !await InstallGame(Path.Combine(path, "PHANTASYSTARONLINE2", "pso2_bin")))
					{
						if (await this.ShowMessageAsync(string.Empty, !Uri.IsWellFormedUriString(path, UriKind.Absolute) ? Text.InvalidPathTryAgain : Text.GoneWrong, AffirmNeg, YesNo) == MessageDialogResult.Negative)
						{
							break;
						}

						path = await this.ShowFolderSelectAsync(string.Empty, Text.SelectInstallLocation, Properties.Resources.DefaultInstallDir);
					}
				}
			}
			else
			{
				await SelectGameFolder();
			}
		}

		private async Task SelectGameFolder()
		{
			var result = MessageDialogResult.Affirmative;

			while (result == MessageDialogResult.Affirmative)
			{
				var gamePath = await this.ShowFileSelectAsync(string.Empty, Text.SelectExe, Properties.Resources.GameFilter);

				if (await Task.Run(() => !string.IsNullOrWhiteSpace(gamePath) && File.Exists(gamePath)))
				{
					Settings.Default.GameFolder = Path.GetDirectoryName(gamePath);
					Settings.Default.IsGameInstalled = true;
					Settings.Default.Save();
					result = MessageDialogResult.Negative;
				}
				else
				{
					result = await this.ShowMessageAsync(string.Empty, Text.InvalidPathTryAgain, AffirmNeg, YesNo);
				}
			}
		}

		private async Task<bool> DownloadLanguagePatch(Uri baseUri, string name, int size, string patchname)
		{
			var url = new Uri(baseUri, name);
			var filepath = Path.Combine(Path.GetTempPath(), name);
			var info = new FileInfo(filepath);

			if (!info.Exists || info.Length != size)
			{
				await _generalDownloadManager.DownloadFileTaskAsync(url, filepath);
			}

			GeneralDownloadProgressbar.IsIndeterminate = true;
			CurrentGeneralDownloadSizeActionLable.Visibility = Visibility.Hidden;
			var succeeded = await Task.Run(() => InstallPatch(filepath, patchname));
			CurrentGeneralDownloadSizeActionLable.Visibility = Visibility.Visible;
			GeneralDownloadProgressbar.IsIndeterminate = false;

			if (succeeded)
			{
				await this.ShowMessageAsync(Text.Complete, string.Format(Text.GenericPatchSuccess, patchname));
			}
			else
			{
				await this.ShowMessageAsync(Text.Failed, string.Format(Text.GenericPatchFailed, patchname));
			}

			info.Refresh();
			if (info.Exists)
			{
				File.Delete(filepath);
			}

			return succeeded;
		}

		private async Task<bool> CheckEnglishPatchVersion(long modtime)
		{
			if (Settings.Default.InstalledEnglishPatch != 0 && Settings.Default.InstalledEnglishPatch < modtime)
			{
				var result = await this.ShowMessageAsync(Text.NewLangPatch, string.Format(Text.NewGenericPatch, "English Patch"), MessageDialogStyle.AffirmativeAndNegative, YesNo);
				return result == MessageDialogResult.Affirmative;
			}

			return false;
		}

		private async Task<bool> CheckLargeFilesVersion(long modtime)
		{
			if (Settings.Default.InstalledLargeFiles != 0 && Settings.Default.InstalledLargeFiles < modtime)
			{
				var result = await this.ShowMessageAsync(Text.NewLangPatch, string.Format(Text.NewGenericPatch, "Large Files"), MessageDialogStyle.AffirmativeAndNegative, YesNo);
				return result == MessageDialogResult.Affirmative;
			}

			return false;
		}

		private async Task DownloadEnglishPatch(string name, int size, long modtime)
		{
			if (await DownloadLanguagePatch(Arghlex, name, size, "EnglishPatch"))
			{
				Settings.Default.InstalledEnglishPatch = modtime;
			}
			else
			{
				EnglishPatchToggle.IsChecked = false;
				Settings.Default.InstalledEnglishPatch = 0;
			}

			Settings.Default.Save();
		}

		private async Task DownloadLargeFiles(string name, int size, long modtime)
		{
			if (await DownloadLanguagePatch(Arghlex, name, size, "LargeFiles"))
			{
				Settings.Default.InstalledLargeFiles = modtime;
			}
			else
			{
				LargeFilesToggle.IsChecked = false;
				Settings.Default.InstalledLargeFiles = 0;
			}

			Settings.Default.Save();
		}

		public void Dispose()
		{
			_generalDownloadManager.Dispose();
		}

		private void AddPluginButton_Click(object sender, RoutedEventArgs e)
		{
			// TODO: Variant here's your button have fun.
		}
	}
}
