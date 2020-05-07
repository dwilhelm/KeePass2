/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2020 Dominik Reichl <dominik.reichl@t-online.de>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using KeePass.App;
using KeePass.App.Configuration;
using KeePass.Resources;
using KeePass.UI;
using KeePass.UI.ToolStripRendering;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Delegates;
using KeePassLib.Serialization;
using KeePassLib.Utility;

using NativeLib = KeePassLib.Native.NativeLib;

namespace KeePass.Forms
{
	/// <summary>
	/// Options dialog. In this dialog, global program options can be configured.
	/// </summary>
	public partial class OptionsForm : Form
	{
		private ImageList m_ilIcons;
		private BannerStyle m_curBannerStyle = BannerStyle.KeePassWin32;
		private bool m_bBlockUIUpdate = false;
		private bool m_bLoadingSettings = false;

		private CheckedLVItemDXList m_cdxSecurityOptions = null;
		private CheckedLVItemDXList m_cdxPolicy = null;
		private CheckedLVItemDXList m_cdxGuiOptions = null;
		private CheckedLVItemDXList m_cdxAdvanced = null;

		private Dictionary<int, string> m_dTsrUuids = new Dictionary<int, string>();
		private int m_argbAltItemBg = 0;
		private Image m_imgAltItemBg = null;

		private Keys m_kPrevAT = Keys.None;
		private Keys m_kPrevATP = Keys.None;
		private Keys m_kPrevATS = Keys.None;
		private Keys m_kPrevSW = Keys.None;

		private AceUrlSchemeOverrides m_aceUrlSchemeOverrides = null;
		private string m_strUrlOverrideAll = string.Empty;

		private string m_strInitialTsRenderer = string.Empty;
		public bool RequiresUIReinitialize
		{
			get { return (Program.Config.UI.ToolStripRenderer != m_strInitialTsRenderer); }
		}

		public OptionsForm()
		{
			InitializeComponent();
			Program.Translation.ApplyTo(this);
		}

		public void InitEx(ImageList ilIcons)
		{
			InitEx(ilIcons, false);
		}

		public void InitEx(ImageList ilIcons, bool bForceInTaskbar)
		{
			Debug.Assert(ilIcons != null);
			m_ilIcons = ilIcons;

			// Set ShowInTaskbar immediately, not later, otherwise the form
			// can disappear:
			// https://sourceforge.net/p/keepass/discussion/329220/thread/c95b5644/
			if(bForceInTaskbar) this.ShowInTaskbar = true;
		}

		private void CreateDialogBanner(BannerStyle bsStyle)
		{
			if(bsStyle == m_curBannerStyle) return;

			m_curBannerStyle = bsStyle;

			m_bannerImage.Image = BannerFactory.CreateBanner(m_bannerImage.Width,
				m_bannerImage.Height, bsStyle,
				Properties.Resources.B48x48_KCMSystem, KPRes.Options,
				KPRes.OptionsDesc);
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			// Can be invoked by tray command; don't use CenterParent
			Debug.Assert(this.StartPosition == FormStartPosition.CenterScreen);

			// When multiline is enabled, tabs added by plugins can result
			// in multiple tab rows, cropping the tab content at the bottom;
			// https://sourceforge.net/p/keepass/discussion/329220/thread/a17a2734/
			Debug.Assert(!m_tabMain.Multiline);

			GlobalWindowManager.AddWindow(this);

			this.Icon = AppIcons.Default;

			Debug.Assert(m_ilIcons != null);
			if(m_ilIcons != null)
			{
				m_tabMain.ImageList = m_ilIcons;

				m_tabSecurity.ImageIndex = (int)PwIcon.TerminalEncrypted;
				m_tabPolicy.ImageIndex = (int)PwIcon.List;
				m_tabGui.ImageIndex = (int)PwIcon.Screen;
				m_tabIntegration.ImageIndex = (int)PwIcon.Console;
				m_tabAdvanced.ImageIndex = (int)PwIcon.ClipboardReady;
			}

			uint uTab = Program.Config.Defaults.OptionsTabIndex;
			if(uTab < (uint)m_tabMain.TabPages.Count)
				m_tabMain.SelectedTab = m_tabMain.TabPages[(int)uTab];

			m_aceUrlSchemeOverrides = Program.Config.Integration.UrlSchemeOverrides.CloneDeep();
			m_strUrlOverrideAll = Program.Config.Integration.UrlOverride;

			Debug.Assert(!m_cmbMenuStyle.Sorted);
			m_cmbMenuStyle.Items.Add(KPRes.Auto + " (" + KPRes.Recommended + ")");
			m_cmbMenuStyle.Items.Add(new string('-', 24));
			int nTsrs = 2, iTsrSel = 0, nSuffixes = 0;
			foreach(TsrFactory fTsr in TsrPool.Factories)
			{
				string strSuffix = string.Empty;
				if(!fTsr.IsSupported())
				{
					strSuffix = " (" + KPRes.IncompatibleEnv + ")";
					++nSuffixes;
				}

				string strUuid = Convert.ToBase64String(fTsr.Uuid.UuidBytes);
				if(Program.Config.UI.ToolStripRenderer == strUuid)
					iTsrSel = nTsrs;

				m_cmbMenuStyle.Items.Add((fTsr.Name ?? string.Empty) + strSuffix);
				m_dTsrUuids[nTsrs] = strUuid;
				++nTsrs;
			}
			Debug.Assert(m_cmbMenuStyle.Items.Count == nTsrs);
			m_cmbMenuStyle.SelectedIndex = iTsrSel;
			if(nSuffixes > 0) m_cmbMenuStyle.DropDownWidth = m_cmbMenuStyle.Width * 2;
			if(AppConfigEx.IsOptionEnforced(Program.Config.UI, "ToolStripRenderer"))
			{
				m_lblMenuStyle.Enabled = false;
				m_cmbMenuStyle.Enabled = false;
			}

			GAction<BannerStyle, string> fAddBannerStyle = delegate(
				BannerStyle bs, string strDisplay)
			{
				Debug.Assert(m_cmbBannerStyle.Items.Count == (long)bs);
				m_cmbBannerStyle.Items.Add(strDisplay);
			};

			Debug.Assert(!m_cmbBannerStyle.Sorted);
			fAddBannerStyle(BannerStyle.Default, KPRes.CurrentStyle);
			fAddBannerStyle(BannerStyle.WinXPLogin, "Windows XP Login");
			fAddBannerStyle(BannerStyle.WinVistaBlack, "Windows Vista Black");
			fAddBannerStyle(BannerStyle.KeePassWin32, "KeePass Win32");
			fAddBannerStyle(BannerStyle.BlueCarbon, "Blue Carbon");

			CreateDialogBanner(BannerStyle.Default); // Default forces generation
			m_cmbBannerStyle.SelectedIndex = (int)BannerStyle.Default;
			if((BannerFactory.CustomGenerator != null) ||
				AppConfigEx.IsOptionEnforced(Program.Config.UI, "BannerStyle"))
			{
				m_lblBannerStyle.Enabled = false;
				m_cmbBannerStyle.Enabled = false;
			}

			AceEscAction aEscCur = Program.Config.MainWindow.EscAction;
			int iEscSel = (int)AceEscAction.Lock;
			GAction<AceEscAction, string> fAddEscAction = delegate(
				AceEscAction aEsc, string strDisplay)
			{
				if(aEsc == aEscCur) iEscSel = m_cmbEscAction.Items.Count;
				Debug.Assert(m_cmbEscAction.Items.Count == (long)aEsc);
				m_cmbEscAction.Items.Add(strDisplay);
			};

			Debug.Assert(!m_cmbEscAction.Sorted);
			fAddEscAction(AceEscAction.None, KPRes.Ignore);
			fAddEscAction(AceEscAction.Lock, KPRes.LockWorkspace);
			fAddEscAction(AceEscAction.Minimize, KPRes.Minimize);
			fAddEscAction(AceEscAction.MinimizeToTray, KPRes.MinimizeToTrayStc);
			fAddEscAction(AceEscAction.Exit, KPRes.Exit);

			m_cmbEscAction.SelectedIndex = iEscSel;

			int nWidth = m_lvPolicy.ClientSize.Width - UIUtil.GetVScrollBarWidth();
			m_lvPolicy.Columns.Add(KPRes.Feature, (nWidth * 10) / 29);
			m_lvPolicy.Columns.Add(KPRes.Description, (nWidth * 19) / 29);

			UIUtil.ConfigureToolTip(m_ttRect);
			m_ttRect.SetToolTip(m_cbClipClearTime, KPRes.ClipboardClearDesc +
				MessageService.NewParagraph + KPRes.ClipboardOptionME);

			if(!NativeLib.IsUnix())
			{
				UIUtil.SetShield(m_btnFileExtCreate, true);
				UIUtil.SetShield(m_btnFileExtRemove, true);

				m_linkHotKeyHelp.Visible = false;
			}
			else // Unix
			{
				m_hkAutoType.TextNone = KPRes.External;
				m_hkAutoTypePassword.TextNone = KPRes.External;
				m_hkAutoTypeSelected.TextNone = KPRes.External;
				m_hkShowWindow.TextNone = KPRes.External;

				m_hkAutoType.Enabled = m_hkAutoTypePassword.Enabled =
					m_hkAutoTypeSelected.Enabled = m_hkShowWindow.Enabled = false;
				m_btnFileExtCreate.Enabled = m_btnFileExtRemove.Enabled = false;
				m_cbAutoRun.Enabled = false;
			}

			UIUtil.SetExplorerTheme(m_lvSecurityOptions, false);
			UIUtil.SetExplorerTheme(m_lvPolicy, false);
			UIUtil.SetExplorerTheme(m_lvGuiOptions, false);
			UIUtil.SetExplorerTheme(m_lvAdvanced, false);

			AppConfigEx.ClearXmlPathCache();

			LoadOptions();

			// if(Program.Config.Meta.IsEnforcedConfiguration)
			//	m_lvPolicy.Enabled = false;

			UpdateUIState();
		}

		private void LoadOptions()
		{
			m_bLoadingSettings = true;

			LoadSecurityOptions();
			LoadPolicyOptions();
			LoadGuiOptions();
			LoadIntegrationOptions();
			LoadAdvancedOptions();

			m_bLoadingSettings = false;
		}

		private void LoadSecurityOptions()
		{
			AceWorkspaceLocking aceWL = Program.Config.Security.WorkspaceLocking;

			uint uLockTime = aceWL.LockAfterTime;
			bool bLockTime = (uLockTime > 0);
			m_numLockAfterTime.Value = (bLockTime ? uLockTime : 300);
			m_cbLockAfterTime.Checked = bLockTime;
			if(AppConfigEx.IsOptionEnforced(aceWL, "LockAfterTime"))
				m_cbLockAfterTime.Enabled = false;

			uLockTime = aceWL.LockAfterGlobalTime;
			bLockTime = (uLockTime > 0);
			m_numLockAfterGlobalTime.Value = (bLockTime ? uLockTime : 240);
			m_cbLockAfterGlobalTime.Checked = bLockTime;
			if(AppConfigEx.IsOptionEnforced(aceWL, "LockAfterGlobalTime"))
				m_cbLockAfterGlobalTime.Enabled = false;

			int nDefaultExpireDays = Program.Config.Defaults.NewEntryExpiresInDays;
			if(nDefaultExpireDays < 0)
				m_cbDefaultExpireDays.Checked = false;
			else
			{
				m_cbDefaultExpireDays.Checked = true;
				try { m_numDefaultExpireDays.Value = nDefaultExpireDays; }
				catch(Exception) { Debug.Assert(false); }
			}
			if(AppConfigEx.IsOptionEnforced(Program.Config.Defaults, "NewEntryExpiresInDays"))
				m_cbDefaultExpireDays.Enabled = false;

			int nClipClear = Program.Config.Security.ClipboardClearAfterSeconds;
			if(nClipClear >= 0)
			{
				m_cbClipClearTime.Checked = true;
				m_numClipClearTime.Value = nClipClear;
			}
			else m_cbClipClearTime.Checked = false;
			if(AppConfigEx.IsOptionEnforced(Program.Config.Security, "ClipboardClearAfterSeconds"))
				m_cbClipClearTime.Enabled = false;

			m_lvSecurityOptions.Columns.Add(string.Empty); // Resize below

			ListViewGroup lvg = new ListViewGroup(KPRes.General);
			m_lvSecurityOptions.Groups.Add(lvg);
			Debug.Assert(lvg.ListView == m_lvSecurityOptions);

			m_cdxSecurityOptions = new CheckedLVItemDXList(m_lvSecurityOptions, true);

			bool? obNoSEv = null; // Allow read-only by enforced config
			string strSEvSuffix = string.Empty;
			if(MonoWorkarounds.IsRequired(1378))
			{
				obNoSEv = true;
				strSEvSuffix = " (" + KPRes.UnsupportedByMono + ")";
			}

			bool? obNoWin = null; // Allow read-only by enforced config
			if(NativeLib.IsUnix()) obNoWin = true;

			m_cdxSecurityOptions.CreateItem(aceWL, "LockOnWindowMinimize",
				lvg, KPRes.LockOnMinimizeTaskbar);
			m_cdxSecurityOptions.CreateItem(aceWL, "LockOnWindowMinimizeToTray",
				lvg, KPRes.LockOnMinimizeTray);
			m_cdxSecurityOptions.CreateItem(aceWL, "LockOnSessionSwitch",
				lvg, KPRes.LockOnSessionSwitch + strSEvSuffix, obNoSEv);
			m_cdxSecurityOptions.CreateItem(aceWL, "LockOnSuspend",
				lvg, KPRes.LockOnSuspend + strSEvSuffix, obNoSEv);
			m_cdxSecurityOptions.CreateItem(aceWL, "LockOnRemoteControlChange",
				lvg, KPRes.LockOnRemoteControlChange + strSEvSuffix, obNoSEv);
			m_cdxSecurityOptions.CreateItem(aceWL, "ExitInsteadOfLockingAfterTime",
				lvg, KPRes.ExitInsteadOfLockingAfterTime);
			m_cdxSecurityOptions.CreateItem(aceWL, "AlwaysExitInsteadOfLocking",
				lvg, KPRes.ExitInsteadOfLockingAlways);

			lvg = new ListViewGroup(KPRes.ClipboardMain);
			m_lvSecurityOptions.Groups.Add(lvg);

			Action<ListViewItem> fClipME = delegate(ListViewItem lvi)
			{
				if(lvi == null) { Debug.Assert(false); return; }
				string str = lvi.Text;
				if(string.IsNullOrEmpty(str)) { Debug.Assert(false); return; }
				lvi.ToolTipText = str + "." + MessageService.NewParagraph +
					KPRes.ClipboardOptionME;
			};

			fClipME(m_cdxSecurityOptions.CreateItem(Program.Config.Security,
				"ClipboardClearOnExit", lvg, KPRes.ClipboardClearOnExit));
			fClipME(m_cdxSecurityOptions.CreateItem(Program.Config.Security,
				"ClipboardNoPersist", lvg, KPRes.ClipboardNoPersist));
			fClipME(m_cdxSecurityOptions.CreateItem(Program.Config.Security,
				"UseClipboardViewerIgnoreFormat", lvg,
				KPRes.ClipboardViewerIgnoreFormat // + " " + KPRes.NotRecommended
				));

			lvg = new ListViewGroup(KPRes.Advanced);
			m_lvSecurityOptions.Groups.Add(lvg);

			if(NativeLib.IsLibraryInstalled())
				m_cdxSecurityOptions.CreateItem(Program.Config.Native, "NativeKeyTransformations",
					lvg, KPRes.NativeLibUse);

			m_cdxSecurityOptions.CreateItem(Program.Config.Security, "MasterKeyOnSecureDesktop",
				lvg, KPRes.MasterKeyOnSecureDesktop, obNoWin);
			m_cdxSecurityOptions.CreateItem(Program.Config.Security, "ClearKeyCommandLineParams",
				lvg, KPRes.ClearKeyCmdLineParams);
			m_cdxSecurityOptions.CreateItem(Program.Config.Security.MasterPassword,
				"RememberWhileOpen", lvg, KPRes.MasterPasswordRmbWhileOpen);

			m_cdxSecurityOptions.UpdateData(false);
			UIUtil.ResizeColumns(m_lvSecurityOptions, true);
		}

		private void LoadPolicyOption(string strPropertyName, AppPolicyId p)
		{
			Debug.Assert(p.ToString() == strPropertyName);

			ListViewItem lvi = m_cdxPolicy.CreateItem(Program.Config.Security.Policy,
				strPropertyName, null, AppPolicy.GetName(p) + "*");
			lvi.SubItems.Add(AppPolicy.GetDesc(p));
		}

		private void LoadPolicyOptions()
		{
			m_cdxPolicy = new CheckedLVItemDXList(m_lvPolicy, true);

			LoadPolicyOption("Plugins", AppPolicyId.Plugins);
			LoadPolicyOption("Export", AppPolicyId.Export);
			LoadPolicyOption("ExportNoKey", AppPolicyId.ExportNoKey);
			LoadPolicyOption("Import", AppPolicyId.Import);
			LoadPolicyOption("Print", AppPolicyId.Print);
			LoadPolicyOption("PrintNoKey", AppPolicyId.PrintNoKey);
			LoadPolicyOption("NewFile", AppPolicyId.NewFile);
			LoadPolicyOption("SaveFile", AppPolicyId.SaveFile);
			LoadPolicyOption("AutoType", AppPolicyId.AutoType);
			LoadPolicyOption("AutoTypeWithoutContext", AppPolicyId.AutoTypeWithoutContext);
			LoadPolicyOption("CopyToClipboard", AppPolicyId.CopyToClipboard);
			LoadPolicyOption("CopyWholeEntries", AppPolicyId.CopyWholeEntries);
			LoadPolicyOption("DragDrop", AppPolicyId.DragDrop);
			LoadPolicyOption("UnhidePasswords", AppPolicyId.UnhidePasswords);
			LoadPolicyOption("ChangeMasterKey", AppPolicyId.ChangeMasterKey);
			LoadPolicyOption("ChangeMasterKeyNoKey", AppPolicyId.ChangeMasterKeyNoKey);
			LoadPolicyOption("EditTriggers", AppPolicyId.EditTriggers);

			m_cdxPolicy.UpdateData(false);
		}

		private void LoadGuiOptions()
		{
			m_strInitialTsRenderer = Program.Config.UI.ToolStripRenderer;

			bool? obNoMin = null;
			if(MonoWorkarounds.IsRequired(1418)) obNoMin = true;

			m_lvGuiOptions.Columns.Add(KPRes.Options); // Resize below

			ListViewGroup lvg = new ListViewGroup(KPRes.MainWindow);
			m_lvGuiOptions.Groups.Add(lvg);
			Debug.Assert(lvg.ListView == m_lvGuiOptions);

			m_cdxGuiOptions = new CheckedLVItemDXList(m_lvGuiOptions, true);

			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "MinimizeToTray",
				lvg, KPRes.MinimizeToTray);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "DropToBackAfterClipboardCopy",
				lvg, KPRes.DropToBackOnCopy);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "MinimizeAfterClipboardCopy",
				lvg, KPRes.MinimizeAfterCopy);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "MinimizeAfterAutoType",
				lvg, KPRes.MinimizeAfterAutoType);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "MinimizeAfterLocking",
				lvg, KPRes.MinimizeAfterLocking);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "MinimizeAfterOpeningDatabase",
				lvg, KPRes.MinimizeAfterOpeningDatabase, obNoMin);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "CloseButtonMinimizesWindow",
				lvg, KPRes.CloseButtonMinimizes);
			// m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "EscMinimizesToTray",
			//	lvg, KPRes.EscMinimizesToTray);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "ShowFullPathInTitle",
				lvg, KPRes.ShowFullPathInTitleBar);
			// m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "ShowFullPathOnTab",
			//	lvg, KPRes.ShowFullPathOnFileTab);
			// m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "ShowDatabaseNameOnTab",
			//	lvg, KPRes.ShowDatabaseNameOnFileTab);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "DisableSaveIfNotModified",
				lvg, KPRes.DisableSaveIfNotModified);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "HideCloseDatabaseButton",
				lvg, KPRes.HideCloseDatabaseTb);
			m_cdxGuiOptions.CreateItem(Program.Config.UI, "ShowAdvAutoTypeCommands",
				lvg, KPRes.ShowAdvAutoTypeCommands);

			lvg = new ListViewGroup(KPRes.EntryList);
			m_lvGuiOptions.Groups.Add(lvg);
			// m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "ShowGridLines",
			//	m_lvGuiOptions, lvg, KPRes.ShowGridLines);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "EntryListAutoResizeColumns",
				lvg, KPRes.EntryListAutoResizeColumns);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "EntryListAlternatingBgColors",
				lvg, KPRes.AlternatingBgColors);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "CopyUrlsInsteadOfOpening",
				lvg, KPRes.CopyUrlsInsteadOfOpening);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "EntrySelGroupSel",
				lvg, KPRes.EntrySelGroupSel);

			if(!Program.Config.MainWindow.EntryListShowDerefData)
			{
				Debug.Assert(!Program.Config.MainWindow.EntryListShowDerefDataAsync);
				Program.Config.MainWindow.EntryListShowDerefDataAsync = false;
			}
			ListViewItem lviDeref = m_cdxGuiOptions.CreateItem(
				Program.Config.MainWindow, "EntryListShowDerefData",
				lvg, KPRes.ShowDerefData + " (" + KPRes.Slow + ")");
			ListViewItem lviDerefAsync = m_cdxGuiOptions.CreateItem(
				Program.Config.MainWindow, "EntryListShowDerefDataAsync",
				lvg, KPRes.ShowDerefDataAsync + " (" + KPRes.IncompatibleWithSorting + ")");
			m_cdxGuiOptions.AddLink(lviDeref, lviDerefAsync, CheckItemLinkType.UncheckedUnchecked);
			m_cdxGuiOptions.AddLink(lviDerefAsync, lviDeref, CheckItemLinkType.CheckedChecked);

			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "EntryListShowDerefDataAndRefs",
				lvg, KPRes.ShowDerefDataAndRefs);

			// lvg = new ListViewGroup(KPRes.EntryView);
			// m_lvGuiOptions.Groups.Add(lvg);
			// m_cdxGuiOptions.CreateItem(Program.Config.MainWindow.EntryView, "HideProtectedCustomStrings",
			//	lvg, KPRes.EntryViewHideProtectedCustomStrings);

			lvg = new ListViewGroup(KPRes.QuickSearchTb);
			m_lvGuiOptions.Groups.Add(lvg);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "QuickFindSearchInPasswords",
				lvg, KPRes.QuickSearchInPwFields);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "QuickFindExcludeExpired",
				lvg, KPRes.QuickSearchExclExpired);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "QuickFindDerefData",
				lvg, KPRes.QuickSearchDerefData + " (" + KPRes.Slow + ")");
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "FocusResultsAfterQuickFind",
				lvg, KPRes.FocusResultsAfterQuickSearch);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "FocusQuickFindOnRestore",
				lvg, KPRes.FocusQuickFindOnRestore);
			m_cdxGuiOptions.CreateItem(Program.Config.MainWindow, "FocusQuickFindOnUntray",
				lvg, KPRes.FocusQuickFindOnUntray);

			lvg = new ListViewGroup(KPRes.TrayIcon);
			m_lvGuiOptions.Groups.Add(lvg);
			// m_cdxGuiOptions.CreateItem(Program.Config.UI.TrayIcon, "ShowOnlyIfTrayedEx",
			//	lvg, KPRes.ShowTrayOnlyIfTrayed);
			m_cdxGuiOptions.CreateItem(Program.Config.UI.TrayIcon, "GrayIcon",
				lvg, KPRes.TrayIconGray);
			m_cdxGuiOptions.CreateItem(Program.Config.UI.TrayIcon, "SingleClickDefault",
				lvg, KPRes.TrayIconSingleClick);

			lvg = new ListViewGroup(KPRes.Dialogs);
			m_lvGuiOptions.Groups.Add(lvg);
			m_cdxGuiOptions.CreateItem(Program.Config.UI, "ShowRecycleConfirmDialog",
				lvg, KPRes.RecycleShowConfirm);
			m_cdxGuiOptions.CreateItem(Program.Config.UI, "ShowDbMntncResultsDialog",
				lvg, KPRes.DbMntncResults);
			m_cdxGuiOptions.CreateItem(Program.Config.UI, "ShowEmSheetDialog",
				lvg, KPRes.EmergencySheetAsk);

			lvg = new ListViewGroup(KPRes.Advanced);
			m_lvGuiOptions.Groups.Add(lvg);
			m_cdxGuiOptions.CreateItem(Program.Config.UI, "RepeatPasswordOnlyWhenHidden",
				lvg, KPRes.RepeatOnlyWhenHidden);
			// m_cdxGuiOptions.CreateItem(Program.Config.UI, "UseCustomToolStripRenderer",
			//	lvg, KPRes.UseCustomToolStripRenderer);
			m_cdxGuiOptions.CreateItem(Program.Config.UI, "TreeViewShowLines",
				lvg, KPRes.TreeViewShowLines);
			m_cdxGuiOptions.CreateItem(Program.Config.UI, "ForceSystemFontUnix",
				lvg, KPRes.ForceSystemFontUnix);

			m_cdxGuiOptions.UpdateData(false);
			UIUtil.ResizeColumns(m_lvGuiOptions, true);

			try { m_numMruCount.Value = Program.Config.Application.MostRecentlyUsed.MaxItemCount; }
			catch(Exception) { Debug.Assert(false); m_numMruCount.Value = AceMru.DefaultMaxItemCount; }
			if(AppConfigEx.IsOptionEnforced(Program.Config.Application.MostRecentlyUsed, "MaxItemCount"))
			{
				m_lblMruCount.Enabled = false;
				m_numMruCount.Enabled = false;
			}

			m_argbAltItemBg = Program.Config.MainWindow.EntryListAlternatingBgColor;
			m_cbCustomAltColor.Checked = (m_argbAltItemBg != 0);
			UpdateButtonImages();

			if(AppConfigEx.IsOptionEnforced(Program.Config.UI, "StandardFont"))
				m_btnSelFont.Enabled = false;
			if(AppConfigEx.IsOptionEnforced(Program.Config.UI, "PasswordFont") ||
				MonoWorkarounds.IsRequired(5795))
				m_btnSelPwFont.Enabled = false;
		}

		private void LoadIntegrationOptions()
		{
			Keys kAT = (Keys)Program.Config.Integration.HotKeyGlobalAutoType;
			m_hkAutoType.HotKey = kAT;
			m_kPrevAT = m_hkAutoType.HotKey; // Adjusted one
			if(AppConfigEx.IsOptionEnforced(Program.Config.Integration, "HotKeyGlobalAutoType"))
				m_hkAutoType.Enabled = false;

			Keys kATP = (Keys)Program.Config.Integration.HotKeyGlobalAutoTypePassword;
			m_hkAutoTypePassword.HotKey = kATP;
			m_kPrevATP = m_hkAutoTypePassword.HotKey; // Adjusted one
			if(AppConfigEx.IsOptionEnforced(Program.Config.Integration, "HotKeyGlobalAutoTypePassword"))
				m_hkAutoTypePassword.Enabled = false;

			Keys kATS = (Keys)Program.Config.Integration.HotKeySelectedAutoType;
			m_hkAutoTypeSelected.HotKey = kATS;
			m_kPrevATS = m_hkAutoTypeSelected.HotKey; // Adjusted one
			if(AppConfigEx.IsOptionEnforced(Program.Config.Integration, "HotKeySelectedAutoType"))
				m_hkAutoTypeSelected.Enabled = false;

			Keys kSW = (Keys)Program.Config.Integration.HotKeyShowWindow;
			m_hkShowWindow.HotKey = kSW;
			m_kPrevSW = m_hkShowWindow.HotKey; // Adjusted one
			if(AppConfigEx.IsOptionEnforced(Program.Config.Integration, "HotKeyShowWindow"))
				m_hkShowWindow.Enabled = false;

			m_cbAutoRun.Checked = ShellUtil.GetStartWithWindows(AppDefs.AutoRunName);

			// m_cbSingleClickTrayAction.Checked = Program.Config.UI.TrayIcon.SingleClickDefault;
			// if(AppConfigEx.IsOptionEnforced(Program.Config.UI.TrayIcon, "SingleClickDefault"))
			//	m_cbSingleClickTrayAction.Enabled = false;
		}

		private void LoadAdvancedOptions()
		{
			bool? obNoMin = null;
			if(MonoWorkarounds.IsRequired(1418)) obNoMin = true;

			m_lvAdvanced.Columns.Add(string.Empty); // Resize below

			m_cdxAdvanced = new CheckedLVItemDXList(m_lvAdvanced, true);

			ListViewGroup lvg = new ListViewGroup(KPRes.StartAndExit);
			m_lvAdvanced.Groups.Add(lvg);
			m_cdxAdvanced.CreateItem(Program.Config.Application.Start, "OpenLastFile",
				lvg, KPRes.AutoRememberOpenLastFile);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "LimitToSingleInstance",
				lvg, KPRes.LimitSingleInstance);
			m_cdxAdvanced.CreateItem(Program.Config.Application.Start, "CheckForUpdate",
				lvg, KPRes.CheckForUpdAtStart);
			m_cdxAdvanced.CreateItem(Program.Config.Application.Start, "MinimizedAndLocked",
				lvg, KPRes.StartMinimizedAndLocked, obNoMin);
			m_cdxAdvanced.CreateItem(Program.Config.Application.FileClosing, "AutoSave",
				lvg, KPRes.AutoSaveAtExit);
			m_cdxAdvanced.CreateItem(Program.Config.Application, "AutoSaveAfterEntryEdit",
				lvg, KPRes.AutoSaveAfterEntryEdit);

			lvg = new ListViewGroup(KPRes.AfterDatabaseOpen);
			m_lvAdvanced.Groups.Add(lvg);
			m_cdxAdvanced.CreateItem(Program.Config.Application.FileOpening, "ShowExpiredEntries",
				lvg, KPRes.AutoShowExpiredEntries);
			m_cdxAdvanced.CreateItem(Program.Config.Application.FileOpening, "ShowSoonToExpireEntries",
				lvg, KPRes.AutoShowSoonToExpireEntries);

			lvg = new ListViewGroup(KPRes.AutoType);
			m_lvAdvanced.Groups.Add(lvg);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeMatchByTitle",
				lvg, KPRes.AutoTypeMatchByTitle);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeMatchByUrlInTitle",
				lvg, KPRes.AutoTypeMatchByUrlInTitle);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeMatchByUrlHostInTitle",
				lvg, KPRes.AutoTypeMatchByUrlHostInTitle);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeMatchByTagInTitle",
				lvg, KPRes.AutoTypeMatchByTagInTitle);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeExpiredCanMatch",
				lvg, KPRes.ExpiredEntriesCanMatch);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeAlwaysShowSelDialog",
				lvg, KPRes.AutoTypeAlwaysShowSelDialog);

			lvg = new ListViewGroup(KPRes.AutoType + " - " + KPRes.SendingNoun);
			m_lvAdvanced.Groups.Add(lvg);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypePrependInitSequenceForIE",
				lvg, KPRes.AutoTypePrependInitSeqForIE);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeReleaseAltWithKeyPress",
				lvg, KPRes.AutoTypeReleaseAltWithKeyPress);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeAdjustKeyboardLayout",
				lvg, KPRes.SameKeybLayout);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeAllowInterleaved",
				lvg, KPRes.InterleavedKeySending);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeCancelOnWindowChange",
				lvg, KPRes.AutoTypeCancelOnWindowChange);
			m_cdxAdvanced.CreateItem(Program.Config.Integration, "AutoTypeCancelOnTitleChange",
				lvg, KPRes.AutoTypeCancelOnTitleChange);

			lvg = new ListViewGroup(KPRes.IOConnectionLong);
			m_lvAdvanced.Groups.Add(lvg);
			m_cdxAdvanced.CreateItem(Program.Config.Application, "VerifyWrittenFileAfterSaving",
				lvg, KPRes.VerifyWrittenFileAfterSave);
			m_cdxAdvanced.CreateItem(Program.Config.Application, "UseTransactedFileWrites",
				lvg, KPRes.UseTransactedDatabaseWrites);
			m_cdxAdvanced.CreateItem(Program.Config.Application, "UseTransactedConfigWrites",
				lvg, KPRes.UseTransactedConfigWrites);
			m_cdxAdvanced.CreateItem(Program.Config.Application, "FileTxExtra",
				lvg, KPRes.FileTxExtra + " (" + KPRes.Slow + ")");
			m_cdxAdvanced.CreateItem(Program.Config.Application, "UseFileLocks",
				lvg, KPRes.UseFileLocks + " " + KPRes.NotRecommended);
			m_cdxAdvanced.CreateItem(Program.Config.Application, "SaveForceSync",
				lvg, KPRes.SaveForceSync);
			m_cdxAdvanced.CreateItem(Program.Config.Security, "SslCertsAcceptInvalid",
				lvg, KPRes.SslCertsAcceptInvalid);

			lvg = new ListViewGroup(KPRes.Advanced);
			m_lvAdvanced.Groups.Add(lvg);

			if(!Program.Config.Integration.SearchKeyFiles)
				Program.Config.Integration.SearchKeyFilesOnRemovableMedia = false;
			ListViewItem lviSearch = m_cdxAdvanced.CreateItem(
				Program.Config.Integration, "SearchKeyFiles",
				lvg, KPRes.SearchKeyFiles);
			ListViewItem lviSearchRmv = m_cdxAdvanced.CreateItem(
				Program.Config.Integration, "SearchKeyFilesOnRemovableMedia",
				lvg, KPRes.SearchKeyFilesAlsoOnRemovable);
			m_cdxAdvanced.AddLink(lviSearch, lviSearchRmv, CheckItemLinkType.UncheckedUnchecked);
			m_cdxAdvanced.AddLink(lviSearchRmv, lviSearch, CheckItemLinkType.CheckedChecked);

			m_cdxAdvanced.CreateItem(Program.Config.Defaults, "RememberKeySources",
				lvg, KPRes.RememberKeySources);
			m_cdxAdvanced.CreateItem(Program.Config.Application, "RememberWorkingDirectories",
				lvg, KPRes.RememberWorkingDirectories);
			m_cdxAdvanced.CreateItem(Program.Config.UI.Hiding, "RememberHidingPasswordsMain",
				lvg, KPRes.RememberHidingPasswordsMain);
			m_cdxAdvanced.CreateItem(Program.Config.UI.Hiding, "SeparateHidingSettings",
				lvg, KPRes.RememberHidingPasswordsEntry);
			m_cdxAdvanced.CreateItem(Program.Config.UI.Hiding, "UnhideButtonAlsoUnhidesSource",
				lvg, KPRes.UnhideSourceCharactersToo);
			m_cdxAdvanced.CreateItem(Program.Config.Defaults, "TanExpiresOnUse",
				lvg, KPRes.TanExpiresOnUse);
			m_cdxAdvanced.CreateItem(Program.Config.Defaults, "RecycleBinCollapse",
				lvg, KPRes.RecycleBinCollapse);
			m_cdxAdvanced.CreateItem(Program.Config.UI, "SecureDesktopPlaySound",
				lvg, KPRes.SecDeskPlaySound);
			m_cdxAdvanced.CreateItem(Program.Config.UI, "OptimizeForScreenReader",
				lvg, KPRes.OptimizeForScreenReader);

			m_cdxAdvanced.UpdateData(false);
			UIUtil.ResizeColumns(m_lvAdvanced, true);

			if(AppConfigEx.IsOptionEnforced(Program.Config.Integration, "ProxyType") ||
				AppConfigEx.IsOptionEnforced(Program.Config.Integration, "ProxyAddress"))
				m_btnProxy.Enabled = false;
		}

		private bool ValidateOptions()
		{
			GFunc<HotKeyControlEx, bool> fAltMod = delegate(HotKeyControlEx c)
			{
				Keys m = (c.HotKey & Keys.Modifiers);
				return ((m == Keys.Alt) || (m == (Keys.Alt | Keys.Shift)));
			};

			if(fAltMod(m_hkAutoType) || fAltMod(m_hkAutoTypePassword) ||
				fAltMod(m_hkAutoTypeSelected) || fAltMod(m_hkShowWindow))
			{
				if(!MessageService.AskYesNo(KPRes.HotKeyAltOnly + MessageService.NewParagraph +
					KPRes.HotKeyAltOnlyHint + MessageService.NewParagraph +
					KPRes.HotKeyAltOnlyQuestion, null, false))
					return false;
			}

			return true;
		}

		private void SaveOptions()
		{
			if(!m_cbLockAfterTime.Checked)
				Program.Config.Security.WorkspaceLocking.LockAfterTime = 0;
			else
				Program.Config.Security.WorkspaceLocking.LockAfterTime =
					(uint)m_numLockAfterTime.Value;

			if(!m_cbLockAfterGlobalTime.Checked)
				Program.Config.Security.WorkspaceLocking.LockAfterGlobalTime = 0;
			else
				Program.Config.Security.WorkspaceLocking.LockAfterGlobalTime =
					(uint)m_numLockAfterGlobalTime.Value;

			if(m_cbDefaultExpireDays.Checked)
				Program.Config.Defaults.NewEntryExpiresInDays =
					(int)m_numDefaultExpireDays.Value;
			else Program.Config.Defaults.NewEntryExpiresInDays = -1;

			if(m_cbClipClearTime.Checked)
				Program.Config.Security.ClipboardClearAfterSeconds =
					(int)m_numClipClearTime.Value;
			else Program.Config.Security.ClipboardClearAfterSeconds = -1;

			m_cdxSecurityOptions.UpdateData(true);

			NativeLib.AllowNative = Program.Config.Native.NativeKeyTransformations;

			m_cdxPolicy.UpdateData(true);
			m_cdxGuiOptions.UpdateData(true);

			string strUuid;
			m_dTsrUuids.TryGetValue(m_cmbMenuStyle.SelectedIndex, out strUuid);
			Program.Config.UI.ToolStripRenderer = (strUuid ?? string.Empty);

			if(m_cmbBannerStyle.SelectedIndex != (int)BannerStyle.Default)
				Program.Config.UI.BannerStyle = (BannerStyle)
					m_cmbBannerStyle.SelectedIndex;

			Program.Config.MainWindow.EscAction =
				(AceEscAction)m_cmbEscAction.SelectedIndex;

			Program.Config.Application.MostRecentlyUsed.MaxItemCount =
				(uint)m_numMruCount.Value;

			Program.Config.MainWindow.EntryListAlternatingBgColor =
				(m_cbCustomAltColor.Checked ? m_argbAltItemBg : 0);

			ChangeHotKey(ref m_kPrevAT, m_hkAutoType,
				AppDefs.GlobalHotKeyId.AutoType);
			ChangeHotKey(ref m_kPrevATP, m_hkAutoTypePassword,
				AppDefs.GlobalHotKeyId.AutoTypePassword);
			ChangeHotKey(ref m_kPrevATS, m_hkAutoTypeSelected,
				AppDefs.GlobalHotKeyId.AutoTypeSelected);
			ChangeHotKey(ref m_kPrevSW, m_hkShowWindow,
				AppDefs.GlobalHotKeyId.ShowWindow);

			// Program.Config.UI.TrayIcon.SingleClickDefault = m_cbSingleClickTrayAction.Checked;

			Program.Config.Integration.UrlSchemeOverrides = m_aceUrlSchemeOverrides;
			Program.Config.Integration.UrlOverride = m_strUrlOverrideAll;

			m_cdxAdvanced.UpdateData(true);

			Program.Config.Apply(AceApplyFlags.All);
		}

		private void CleanUpEx()
		{
			int nTab = m_tabMain.SelectedIndex;
			if((nTab >= 0) && (nTab < m_tabMain.TabPages.Count))
				Program.Config.Defaults.OptionsTabIndex = (uint)nTab;

			m_tabMain.ImageList = null; // Detach event handlers

			UIUtil.DisposeButtonImage(m_btnCustomAltColor, ref m_imgAltItemBg);

			m_cdxSecurityOptions.Release();
			m_cdxPolicy.Release();
			m_cdxGuiOptions.Release();
			m_cdxAdvanced.Release();

			AppConfigEx.ClearXmlPathCache();
		}

		private static void ChangeHotKey(ref Keys kPrev, HotKeyControlEx hkControl,
			int nHotKeyID)
		{
			Keys kNew = hkControl.HotKey;
			if(kNew == kPrev) return;

			kPrev = kNew;

			if(nHotKeyID == AppDefs.GlobalHotKeyId.AutoType)
				Program.Config.Integration.HotKeyGlobalAutoType = (long)kNew;
			else if(nHotKeyID == AppDefs.GlobalHotKeyId.AutoTypePassword)
				Program.Config.Integration.HotKeyGlobalAutoTypePassword = (long)kNew;
			else if(nHotKeyID == AppDefs.GlobalHotKeyId.AutoTypeSelected)
				Program.Config.Integration.HotKeySelectedAutoType = (long)kNew;
			else if(nHotKeyID == AppDefs.GlobalHotKeyId.ShowWindow)
				Program.Config.Integration.HotKeyShowWindow = (long)kNew;
			else { Debug.Assert(false); }

			HotKeyManager.UnregisterHotKey(nHotKeyID);
			if(kNew != Keys.None)
				HotKeyManager.RegisterHotKey(nHotKeyID, kNew);
		}

		private void UpdateUIState()
		{
			if(m_bBlockUIUpdate) return;
			m_bBlockUIUpdate = true;

			m_numLockAfterTime.Enabled = (m_cbLockAfterTime.Checked &&
				m_cbLockAfterTime.Enabled);

			if(WinUtil.IsWindows9x || NativeLib.IsUnix())
			{
				m_cbLockAfterGlobalTime.Checked = false;
				m_cbLockAfterGlobalTime.Enabled = false;
				m_numLockAfterGlobalTime.Enabled = false;
			}
			else
				m_numLockAfterGlobalTime.Enabled = (m_cbLockAfterGlobalTime.Checked &&
					m_cbLockAfterGlobalTime.Enabled);

			m_numDefaultExpireDays.Enabled = (m_cbDefaultExpireDays.Checked &&
				m_cbDefaultExpireDays.Enabled);
			m_numClipClearTime.Enabled = (m_cbClipClearTime.Checked &&
				m_cbClipClearTime.Enabled);

			m_btnCustomAltColor.Enabled = m_cbCustomAltColor.Checked;

			m_bBlockUIUpdate = false;
		}

		private void OnBtnOK(object sender, EventArgs e)
		{
			if(!ValidateOptions()) { this.DialogResult = DialogResult.None; return; }

			SaveOptions();
		}

		private void OnBtnCancel(object sender, EventArgs e)
		{
		}

		private void OnBannerStyleSelectedChanged(object sender, EventArgs e)
		{
			int nIndex = m_cmbBannerStyle.SelectedIndex;
			
			BannerStyle bs = (BannerStyle)nIndex;
			CreateDialogBanner(bs);
		}

		private void OnLockAfterTimeCheckedChanged(object sender, EventArgs e)
		{
			UpdateUIState();
		}

		private void OnBtnSelListFont(object sender, EventArgs e)
		{
			FontDialog dlg = UIUtil.CreateFontDialog(false);

			AceFont fOld = Program.Config.UI.StandardFont;
			if(fOld.OverrideUIDefault) dlg.Font = fOld.ToFont();
			else
			{
				try { dlg.Font = m_lvSecurityOptions.Font; }
				catch(Exception) { Debug.Assert(false); }
			}

			if(dlg.ShowDialog() == DialogResult.OK)
			{
				Program.Config.UI.StandardFont = new AceFont(dlg.Font);
				Program.Config.UI.StandardFont.OverrideUIDefault = true;
			}
			dlg.Dispose();
		}

		private void OnBtnSelPwFont(object sender, EventArgs e)
		{
			FontDialog dlg = UIUtil.CreateFontDialog(false);

			AceFont fOld = Program.Config.UI.PasswordFont;
			if(fOld.OverrideUIDefault) dlg.Font = fOld.ToFont();
			else if(FontUtil.MonoFont != null) dlg.Font = FontUtil.MonoFont;
			else
			{
				try
				{
					dlg.Font = new Font(FontFamily.GenericMonospace,
						m_lvSecurityOptions.Font.SizeInPoints);
				}
				catch(Exception) { Debug.Assert(false); }
			}

			if(dlg.ShowDialog() == DialogResult.OK)
			{
				Program.Config.UI.PasswordFont = new AceFont(dlg.Font);
				Program.Config.UI.PasswordFont.OverrideUIDefault = true;
			}
			dlg.Dispose();
		}

		private void OnDefaultExpireDaysCheckedChanged(object sender, EventArgs e)
		{
			UpdateUIState();
		}

		private void OnBtnFileExtCreate(object sender, EventArgs e)
		{
			// ShellUtil.RegisterExtension(AppDefs.FileExtension.FileExt, AppDefs.FileExtension.ExtId,
			//	KPRes.FileExtName2, WinUtil.GetExecutable(), PwDefs.ShortProductName, true);
			WinUtil.RunElevated(WinUtil.GetExecutable(), "-" +
				AppDefs.CommandLineOptions.FileExtRegister, false);
		}

		private void OnBtnFileExtRemove(object sender, EventArgs e)
		{
			// ShellUtil.UnregisterExtension(AppDefs.FileExtension.FileExt,
			//	AppDefs.FileExtension.ExtId);
			WinUtil.RunElevated(WinUtil.GetExecutable(), "-" +
				AppDefs.CommandLineOptions.FileExtUnregister, false);
		}

		private void OnCheckedChangedAutoRun(object sender, EventArgs e)
		{
			if(m_bLoadingSettings) return;

			bool bRequested = m_cbAutoRun.Checked;
			bool bCurrent = ShellUtil.GetStartWithWindows(AppDefs.AutoRunName);

			if(bRequested != bCurrent)
			{
				string strPath = WinUtil.GetExecutable().Trim();
				if(strPath.StartsWith("\"") == false)
					strPath = "\"" + strPath + "\"";
				ShellUtil.SetStartWithWindows(AppDefs.AutoRunName, strPath,
					bRequested);

				bool bNew = ShellUtil.GetStartWithWindows(AppDefs.AutoRunName);

				if(bNew != bRequested)
					m_cbAutoRun.Checked = bNew;
			}
		}

		private void OnFormClosed(object sender, FormClosedEventArgs e)
		{
			CleanUpEx();
			GlobalWindowManager.RemoveWindow(this);
		}

		private void OnPolicyInfoLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			AppHelp.ShowHelp(AppDefs.HelpTopics.AppPolicy, null);
		}

		private void OnClipboardClearTimeCheckedChanged(object sender, EventArgs e)
		{
			UpdateUIState();
		}

		private void OnBtnUrlOverrides(object sender, EventArgs e)
		{
			UrlOverridesForm dlg = new UrlOverridesForm();
			dlg.InitEx(m_aceUrlSchemeOverrides, m_strUrlOverrideAll);

			if(dlg.ShowDialog() == DialogResult.OK)
				m_strUrlOverrideAll = dlg.UrlOverrideAll;

			UIUtil.DestroyForm(dlg);
		}

		private void OnHotKeyHelpLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			AppHelp.ShowHelp(AppDefs.HelpTopics.Setup, AppDefs.HelpTopics.SetupMono);
		}

		private void OnLockAfterGlobalTimeCheckedChanged(object sender, EventArgs e)
		{
			UpdateUIState();
		}

		private void OnBtnProxy(object sender, EventArgs e)
		{
			ProxyForm dlg = new ProxyForm();
			UIUtil.ShowDialogAndDestroy(dlg);
		}

		private void UpdateButtonImages()
		{
			if(m_argbAltItemBg != 0)
			{
				Color clr = Color.FromArgb(m_argbAltItemBg);
				Image imgNew = UIUtil.CreateColorBitmap24(m_btnCustomAltColor, clr);
				UIUtil.OverwriteButtonImage(m_btnCustomAltColor, ref m_imgAltItemBg,
					imgNew);
			}
		}

		private void OnCustomAltColorCheckedChanged(object sender, EventArgs e)
		{
			UpdateUIState();
		}

		private void OnBtnCustomAltColor(object sender, EventArgs e)
		{
			Color clrCur = UIUtil.GetAlternateColor(m_lvGuiOptions.BackColor);
			if(m_argbAltItemBg != 0) clrCur = Color.FromArgb(m_argbAltItemBg);

			Color? clr = UIUtil.ShowColorDialog(clrCur);
			if(clr.HasValue)
			{
				m_argbAltItemBg = clr.Value.ToArgb();
				UpdateButtonImages();
			}
		}
	}
}
