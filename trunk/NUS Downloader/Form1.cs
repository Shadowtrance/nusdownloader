﻿///////////////////////////////////////////
// NUS Downloader: Form1.cs              //
// $Rev::                              $ //
// $Author::                           $ //
// $Date::                             $ //
///////////////////////////////////////////

///////////////////////////////////////
// Copyright (C) 2010
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
///////////////////////////////////////


using System;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Xml;
using System.Drawing;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Threading;
using System.Text;
using System.Diagnostics;

namespace NUS_Downloader
{
     partial class Form1 : Form
    {
        private readonly string CURRENT_DIR = Directory.GetCurrentDirectory();

#if DEBUG
        private static string svnversion = "$Rev$";
        private static string version = String.Format("SVN r{0}", ((int.Parse(svnversion.Replace("$"+"R"+"e"+"v"+": ","").Replace(" "+"$","")))+1));
#else
        // TODO: Always remember to change version!
        private string version = "v2.0 Beta";
#endif

        private static bool dsidecrypt = false;

        // Cross-thread Windows Formsing
        private delegate void AddToolStripItemToStripCallback(
            ToolStripMenuItem menulist, ToolStripMenuItem additionitem);
        private delegate void WriteStatusCallback(string Update, Color writecolor);
        private delegate void BootChecksCallback();
        private delegate void SetEnableForDownloadCallback(bool enabled);
        private delegate void SetPropertyThreadSafeCallback(System.ComponentModel.Component what, object setto, string property);
        private delegate string OfficialWADNamingCallback(string whut);

        // Images do not compare unless globalized...
        private Image green = Properties.Resources.bullet_green;
        private Image orange = Properties.Resources.bullet_orange;
        private Image redorb = Properties.Resources.bullet_red;
        private Image redgreen = Properties.Resources.bullet_redgreen;
        private Image redorange = Properties.Resources.bullet_redorange;

        private string WAD_Saveas_Filename;

        // TODO: OOP scripting
        private string script_filename;
        private bool script_mode = false;
        private string[] nusentries;

        // Proxy stuff...
        private string proxy_url;
        private string proxy_usr;
        private string proxy_pwd;

        // Database thread
        private BackgroundWorker fds;

        // Scripts Thread
        private BackgroundWorker scriptsWorker;

        // Colours for status box
        private System.Drawing.Color normalcolor = Color.FromName("Black");
        private System.Drawing.Color warningcolor = Color.FromName("DarkGoldenrod");
        private System.Drawing.Color errorcolor = Color.FromName("Crimson");
        private System.Drawing.Color infocolor = Color.FromName("RoyalBlue");

        // This is the standard entry to the GUI
        public Form1()
        {
            this.Font = new System.Drawing.Font("Tahoma", 8);
            InitializeComponent();
            this.MaximumSize = this.MinimumSize = this.Size; // Lock size down PATCHOW :D
            if (Type.GetType("Mono.Runtime") != null)
            {
                saveaswadbtn.Text = "Save As";
                clearButton.Text = "Clear";
                keepenccontents.Text = "Keep Enc. Contents";
                clearButton.Left -= 41;
            }
            else
                statusbox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7);
            statusbox.SelectionColor = statusbox.ForeColor = normalcolor;
            if (version.StartsWith("SVN"))
            {
                WriteStatus("!!!!! THIS IS A DEBUG BUILD FROM SVN !!!!!", warningcolor);
                WriteStatus("Features CAN and WILL be broken in this build", warningcolor);
                WriteStatus("REMEMBER TO CHANGE TO THE RELEASE CONFIGURATION AND CHANGE VERSION NUMBER BEFORE BUILDING!", warningcolor);
                WriteStatus("\r\n");
            }
            /*
            KoreaMassUpdate.DropDownItemClicked += new ToolStripItemClickedEventHandler(upditem_itemclicked);
            NTSCMassUpdate.DropDownItemClicked += new ToolStripItemClickedEventHandler(upditem_itemclicked);
            PALMassUpdate.DropDownItemClicked += new ToolStripItemClickedEventHandler(upditem_itemclicked);*/

            // Database BGLoader
            this.fds = new BackgroundWorker();
            this.fds.DoWork += new DoWorkEventHandler(DoAllDatabaseyStuff);
            this.fds.RunWorkerCompleted += new RunWorkerCompletedEventHandler(DoAllDatabaseyStuff_Completed);
            this.fds.ProgressChanged += new ProgressChangedEventHandler(DoAllDatabaseyStuff_ProgressChanged);
            this.fds.WorkerReportsProgress = true;

            // Scripts BGLoader
            this.scriptsWorker = new BackgroundWorker();
            this.scriptsWorker.DoWork += new DoWorkEventHandler(OrganizeScripts);
            this.scriptsWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(scriptsWorker_RunWorkerCompleted);
            RunScriptOrganizer();

            BootChecks();
        }

        // CLI Mode
        public Form1(string[] args)
        {
            InitializeComponent();
            Application.DoEvents();

            BootChecks();

            // Fix proxy entry.
            if (!(String.IsNullOrEmpty(proxy_url)))
                while (String.IsNullOrEmpty(proxy_pwd))
                    Thread.Sleep(1000);

            if ((args.Length == 1) && (File.Exists(args[0])))
            {
                script_filename = args[0];
                BackgroundWorker scripter = new BackgroundWorker();
                scripter.DoWork += new DoWorkEventHandler(RunScript);
                scripter.RunWorkerAsync();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.Text = String.Format("NUSD - {0}", version); ;
            this.Size = this.MinimumSize;
            consoleCBox.SelectedIndex = 0;
        }

        private bool NUSDFileExists(string filename)
        {
            return File.Exists(Path.Combine(CURRENT_DIR, filename));
        }

        /// <summary>
        /// Checks certain file existances, etc.
        /// </summary>
        /// <returns></returns>
        private void BootChecks()
        {
            //Check if correct thread...
            if (this.InvokeRequired)
            {
                Debug.WriteLine("InvokeRequired...");
                BootChecksCallback bcc = new BootChecksCallback(BootChecks);
                this.Invoke(bcc);
                return;
            }

            // Check for DSi common key bin file...
            if (NUSDFileExists("dsikey.bin") == true)
            {
                WriteStatus("DSi Common Key detected.");
                dsidecrypt = true;
            }

            // Check for database.xml
            if (NUSDFileExists("database.xml") == false)
            {
                WriteStatus("Database.xml not found. Title database not usable!");
                /*databaseButton.Click -= new System.EventHandler(this.button4_Click);
                databaseButton.Click += new System.EventHandler(this.updateDatabaseToolStripMenuItem_Click);
                databaseButton.Text = "Download DB"; */
                DatabaseEnabled(false);
                updateDatabaseToolStripMenuItem.Enabled = true;
                updateDatabaseToolStripMenuItem.Visible = true;
                updateDatabaseToolStripMenuItem.Text = "Download Database";
            }
            else
            {
                Database db = new Database();
                db.LoadDatabaseToStream(Path.Combine(CURRENT_DIR, "database.xml"));
                string version = db.GetDatabaseVersion();
                WriteStatus("Database.xml detected.");
                WriteStatus(" - Version: " + version);
                updateDatabaseToolStripMenuItem.Text = "Update Database";
                //databaseButton.Enabled = false;
                //databaseButton.Text = "DB Loading";
                databaseButton.Text = "  [    ]";
                databaseButton.Image = Properties.Resources.arrow_ticker;
                // Load it up...
                this.fds.RunWorkerAsync();
            }

            // Check for Proxy Settings file...
            if (NUSDFileExists("proxy.txt") == true)
            {
                WriteStatus("Proxy settings detected.");
                string[] proxy_file = File.ReadAllLines(Path.Combine(CURRENT_DIR, "proxy.txt"));
                proxy_url = proxy_file[0];

                // If proxy\nuser\npassword
                if (proxy_file.Length > 2)
                {
                    proxy_usr = proxy_file[1];
                    proxy_pwd = proxy_file[2];
                }
                else if (proxy_file.Length > 1)
                {
                    proxy_usr = proxy_file[1];
                    SetAllEnabled(false);
                    ProxyVerifyBox.Visible = true;
                    ProxyVerifyBox.Enabled = true;
                    ProxyPwdBox.Enabled = true;
                    SaveProxyBtn.Enabled = true;
                    ProxyVerifyBox.Select();
                }
            }
        }

        private void DoAllDatabaseyStuff(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            ClearDatabaseStrip();
            FillDatabaseStrip(worker);
            LoadRegionCodes();
            FillDatabaseScripts();
            ShowInnerToolTips(false);
        }

        private void DoAllDatabaseyStuff_Completed(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            //this.databaseButton.Enabled = true;
            this.databaseButton.Text = "Database...";
            this.databaseButton.Image = null;
            /*
            if (this.KoreaMassUpdate.HasDropDownItems || this.PALMassUpdate.HasDropDownItems || this.NTSCMassUpdate.HasDropDownItems)
            {
                this.scriptsbutton.Enabled = true;
            }*/
        }

        private void DoAllDatabaseyStuff_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage == 25)
                databaseButton.Text = "  [.   ]";
            else if (e.ProgressPercentage == 25)
                databaseButton.Text = "  [..  ]";
            else if (e.ProgressPercentage == 75)
                databaseButton.Text = "  [... ]";
            else if (e.ProgressPercentage == 100)
                databaseButton.Text = "  [....]";
        }

        private void RunScriptOrganizer()
        {
            this.scriptsWorker.RunWorkerAsync();
        }

        private void SetAllEnabled(bool enabled)
        {
            for (int a = 0; a < this.Controls.Count; a++)
            {
                try
                {
                    this.Controls[a].Enabled = enabled;
                }
                catch
                {
                    // ...
                }
            }
        }

        /*
        /// <summary>
        /// Gets the database version.
        /// </summary>
        /// <param name="file">The database file.</param>
        /// <returns></returns>
        private string GetDatabaseVersion(string file)
        {
            // Read version of Database.xml
            XmlDocument xDoc = new XmlDocument();
            if (file.Contains("<"))
                xDoc.LoadXml(file);
            else
            {
                if (File.Exists(file))
                {
                    xDoc.Load(file);
                }
                else
                {
                    return "None Found";
                }
            }
            XmlNodeList DatabaseList = xDoc.GetElementsByTagName("database");
            XmlAttributeCollection Attributes = DatabaseList[0].Attributes;
            return Attributes[0].Value;
        }*/

        private void extrasMenuButton_Click(object sender, EventArgs e)
        {
            // Show extras menu
            extrasStrip.Show(Extrasbtn, 2, (2+Extrasbtn.Height));
        }

        /// <summary>
        /// Loads the title info from TMD.
        /// </summary>
        private void LoadTitleFromTMD()
        {
            // Show dialog for opening TMD file...
            OpenFileDialog opentmd = new OpenFileDialog();
            opentmd.Filter = "TMD Files|tmd";
            opentmd.Title = "Open TMD";
            if (opentmd.ShowDialog() != DialogResult.Cancel)
            {
                libWiiSharp.TMD tmdLocal = new libWiiSharp.TMD();
                tmdLocal.LoadFile(opentmd.FileName);
                WriteStatus(String.Format("TMD Loaded ({0} blocks)", tmdLocal.GetNandBlocks()));

                titleidbox.Text = tmdLocal.TitleID.ToString("X16");
                WriteStatus("Title ID: " + tmdLocal.TitleID.ToString("X16"));

                titleversion.Text = tmdLocal.TitleVersion.ToString();
                WriteStatus("Version: " + tmdLocal.TitleVersion);

                if (tmdLocal.StartupIOS.ToString("X") != "0")
                    WriteStatus("Requires: IOS" + int.Parse(tmdLocal.StartupIOS.ToString("X").Substring(7, 2).ToString(), System.Globalization.NumberStyles.HexNumber));
                
                WriteStatus("Content Count: " + tmdLocal.NumOfContents);

                for (int a = 0; a < tmdLocal.Contents.Length; a++)
			    {
                    WriteStatus(String.Format("   Content {0}: {1} ({2} bytes)", a, tmdLocal.Contents[a].ContentID.ToString("X8"), tmdLocal.Contents[a].Size.ToString()));
                    WriteStatus(String.Format("    - Index: {0}", tmdLocal.Contents[a].Index.ToString()));
                    WriteStatus(String.Format("    - Type: {0}", tmdLocal.Contents[a].Type.ToString()));
                    WriteStatus(String.Format("    - Hash: {0}...", DisplayBytes(tmdLocal.Contents[a].Hash, String.Empty).Substring(0, 8)));
                }

                WriteStatus("TMD information parsed!");
            }
        }

        /// <summary>
        /// Writes the status to the statusbox.
        /// </summary>
        /// <param name="Update">The update.</param>
        /// <param name="writecolor">The color to use for writing text into the text box.</param>
        public void WriteStatus(string Update, Color writecolor)
        {
            // Check if thread-safe
            if (statusbox.InvokeRequired)
            {
                Debug.WriteLine("InvokeRequired...");
                WriteStatusCallback wsc = new WriteStatusCallback(WriteStatus);
                this.Invoke(wsc, new object[] { Update, writecolor });
                return;
            }
            // Small function for writing text to the statusbox...
            int startlen = statusbox.TextLength;
            if (statusbox.Text == "")
                statusbox.Text = Update;
            else
                statusbox.AppendText("\r\n" + Update);
            int endlen = statusbox.TextLength;

            // Set the color
            statusbox.Select(startlen, endlen - startlen);
            statusbox.SelectionColor = writecolor;

            // Scroll to end of text box.
            statusbox.SelectionStart = statusbox.TextLength;
            statusbox.SelectionLength = 0;
            statusbox.ScrollToCaret();
        }

        /// <summary>
        /// Writes the status to the statusbox.
        /// </summary>
        /// <param name="Update">The update.</param>
        public void WriteStatus(string Update)
        {
            WriteStatus(Update, normalcolor);
        }

        /// <summary>
        /// Reads the type of the Title ID.
        /// </summary>
        /// <param name="ttlid">The TitleID.</param>
        private void ReadIDType(string ttlid)
        {
            /*  Wiibrew TitleID Info...
                # 3 00000001: Essential system titles
                # 4 00010000 and 00010004 : Disc-based games
                # 5 00010001: Downloaded channels

                    * 5.1 000010001-Cxxx : Commodore 64 Games
                    * 5.2 000010001-Exxx : NeoGeo Games
                    * 5.3 000010001-Fxxx : NES Games
                    * 5.4 000010001-Hxxx : Channels
                    * 5.5 000010001-Jxxx : SNES Games
                    * 5.6 000010001-Nxxx : Nintendo 64 Games
                    * 5.7 000010001-Wxxx : WiiWare

                # 6 00010002: System channels
                # 7 00010004: Game channels and games that use them
                # 8 00010005: Downloaded Game Content
                # 9 00010008: "Hidden" channels
             */

            if (ttlid.Substring(0, 8) == "00000001")
                WriteStatus("ID Type: System Title. BE CAREFUL!", warningcolor);
            else if ((ttlid.Substring(0, 8) == "00010000") || (ttlid.Substring(0, 8) == "00010004"))
                WriteStatus("ID Type: Disc-Based Game. Unlikely NUS Content!");
            else if (ttlid.Substring(0, 8) == "00010001")
                WriteStatus("ID Type: Downloaded Channel. Possible NUS Content.");
            else if (ttlid.Substring(0, 8) == "00010002")
                WriteStatus("ID Type: System Channel. BE CAREFUL!", warningcolor);
            else if (ttlid.Substring(0, 8) == "00010004")
                WriteStatus("ID Type: Game Channel. Unlikely NUS Content!");
            else if (ttlid.Substring(0, 8) == "00010005")
                WriteStatus("ID Type: Downloaded Game Content. Unlikely NUS Content!");
            else if (ttlid.Substring(0, 8) == "00010008")
                WriteStatus("ID Type: 'Hidden' Channel. Unlikely NUS Content!");
            else
                WriteStatus("ID Type: Unknown. Unlikely NUS Content!");
        }

        private void DownloadBtn_Click(object sender, EventArgs e)
        {
            if (titleidbox.Text == String.Empty)
            {
                // Prevent mass deletion and fail
                WriteStatus("Please enter a Title ID!", errorcolor);
                return;
            }
            else if (!(packbox.Checked) && !(decryptbox.Checked) && !(keepenccontents.Checked))
            {
                // Prevent pointless running by n00bs.
                WriteStatus("Running with your current settings will produce no output!", errorcolor);
                WriteStatus(" - To amend this, look below and check an output type.", errorcolor);
                return;
            }
            else if (!(script_mode))
            {
                try
                {
                    if (!statusbox.Lines[0].StartsWith(" ---"))
                        SetTextThreadSafe(statusbox, " --- " + titleidbox.Text + " ---");
                }
                catch // No lines present...
                {
                    SetTextThreadSafe(statusbox, " --- " + titleidbox.Text + " ---");
                }
            }
            else
                WriteStatus(" --- " + titleidbox.Text + " ---");
           

            // Running Downloads in background so no form freezing
            NUSDownloader.RunWorkerAsync();
        }

        private void SetTextThreadSafe(System.Windows.Forms.Control what, string setto)
        {
            SetPropertyThreadSafe(what, "Name", setto);
        }

        private void SetPropertyThreadSafe(System.ComponentModel.Component what, object setto, string property)
        {
            if (this.InvokeRequired)
            {
                SetPropertyThreadSafeCallback sptscb = new SetPropertyThreadSafeCallback(SetPropertyThreadSafe);
                try
                {
                    this.Invoke(sptscb, new object[] { what, setto, property });
                }
                catch (Exception)
                {
                    // FFFFF!
                }
                return;
            }
            what.GetType().GetProperty(property).SetValue(what, setto, null);
            //what.Text = setto;
        }

        private void NUSDownloader_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            Control.CheckForIllegalCrossThreadCalls = false; // this function would need major rewriting to get rid of this...
            if (!(script_mode))
                WriteStatus("Starting NUS Download. Please be patient!", infocolor);
            SetEnableforDownload(false);
            downloadstartbtn.Text = "Starting NUS Download!";

            // WebClient configuration
            WebClient nusWC = new WebClient();
            nusWC = ConfigureWithProxy(nusWC);
            nusWC.Headers.Add("User-Agent", "wii libnup/1.0"); // Set UserAgent to Wii value
            
            // Create\Configure NusClient
            libWiiSharp.NusClient nusClient = new libWiiSharp.NusClient();
            nusClient.ConfigureNusClient(nusWC);
            nusClient.UseLocalFiles = localuse.Checked;
            nusClient.ContinueWithoutTicket = true;

            // Server
            if (consoleCBox.SelectedIndex == 0)
                nusClient.SetToWiiServer();
            else if (consoleCBox.SelectedIndex == 1)
                nusClient.SetToDSiServer();

            // Events
            nusClient.Debug += new EventHandler<libWiiSharp.MessageEventArgs>(nusClient_Debug);
            nusClient.Progress += new EventHandler<ProgressChangedEventArgs>(nusClient_Progress);

            libWiiSharp.StoreType[] storeTypes = new libWiiSharp.StoreType[3];
            if (packbox.Checked) storeTypes[0] = libWiiSharp.StoreType.WAD; else storeTypes[0] = libWiiSharp.StoreType.Empty;
            if (decryptbox.Checked) storeTypes[1] = libWiiSharp.StoreType.DecryptedContent; else storeTypes[1] = libWiiSharp.StoreType.Empty;
            if (keepenccontents.Checked) storeTypes[2] = libWiiSharp.StoreType.EncryptedContent; else storeTypes[2] = libWiiSharp.StoreType.Empty;

            string wadName;
            if (String.IsNullOrEmpty(WAD_Saveas_Filename))
                wadName = wadnamebox.Text;
            else
                wadName = WAD_Saveas_Filename;

            try
            {
                nusClient.DownloadTitle(titleidbox.Text, titleversion.Text, Path.Combine(CURRENT_DIR, "titles"), wadName, storeTypes);
            }
            catch (Exception ex)
            {
                WriteStatus("Uhoh, the download bombed: \"" + ex.Message + " ):\"", errorcolor);
            }

            if (iosPatchCheckbox.Checked == true) { // Apply patches then...
                bool didpatch = false;
                int noofpatches = 0;
                string appendpatch = "";
                // Okay, it's checked.
                libWiiSharp.IosPatcher iosp = new libWiiSharp.IosPatcher();
                libWiiSharp.WAD ioswad = new libWiiSharp.WAD();
                wadName = wadName.Replace("[v]", nusClient.TitleVersion.ToString());
                if (wadName.Contains(Path.DirectorySeparatorChar.ToString()) || wadName.Contains(Path.AltDirectorySeparatorChar.ToString()))
                    ioswad.LoadFile(wadName);
                else
                    ioswad.LoadFile(Path.Combine(Path.Combine(Path.Combine(Path.Combine(CURRENT_DIR, "titles"), titleidbox.Text), nusClient.TitleVersion.ToString()), wadName));
                try
                {
                    iosp.LoadIOS(ref ioswad);
                }
                catch (Exception)
                {
                    WriteStatus("NUS Download Finished.", infocolor);
                    return;
                }
                foreach (object checkItem in iosPatchesListBox.CheckedItems)
                {
                    // ensure not 'indeterminate'
                    if (iosPatchesListBox.GetItemCheckState(iosPatchesListBox.Items.IndexOf(checkItem)).ToString() == "Checked") {
                        switch (checkItem.ToString()) {
                            case "Trucha bug":
                                noofpatches = iosp.PatchFakeSigning();
                                if (noofpatches > 0)
                                {
                                    WriteStatus(" - Patched in fake-signing:", infocolor);
                                    if (noofpatches > 1)
                                        appendpatch = "es";
                                    else
                                        appendpatch = "";
                                    WriteStatus(String.Format("     {0} patch{1} applied.", noofpatches, appendpatch));
                                    didpatch = true;
                                }
                                else
                                    WriteStatus(" - Could not patch fake-signing", errorcolor);
                                break;
                            case "ES_Identify":
                                noofpatches = iosp.PatchEsIdentify();
                                if (noofpatches > 0)
                                {
                                    WriteStatus(" - Patched in ES_Identify:", infocolor);
                                    if (noofpatches > 1)
                                        appendpatch = "es";
                                    else
                                        appendpatch = "";
                                    WriteStatus(String.Format("     {0} patch{1} applied.", noofpatches, appendpatch));
                                    didpatch = true;
                                }
                                else
                                    WriteStatus(" - Could not patch ES_Identify", errorcolor);
                                break;
                            case "NAND permissions":
                                noofpatches = iosp.PatchNandPermissions();
                                if (noofpatches > 0)
                                {
                                    WriteStatus(" - Patched in NAND permissions:", infocolor);
                                    if (noofpatches > 1)
                                        appendpatch = "es";
                                    else
                                        appendpatch = "";
                                    WriteStatus(String.Format("     {0} patch{1} applied.", noofpatches, appendpatch));
                                    didpatch = true;
                                }
                                else
                                    WriteStatus(" - Could not patch NAND permissions", errorcolor);
                                break;
                        }
                    }
                    else {
                    //    WriteStatus(iosPatchesListBox.GetItemCheckState(iosPatchesListBox.Items.IndexOf(checkItem)).ToString());
                    }
                }
                if (didpatch)
                {
                    wadName = wadName.Replace(".wad",".patched.wad");
                    try
                    {
                        if (wadName.Contains(Path.DirectorySeparatorChar.ToString()) || wadName.Contains(Path.AltDirectorySeparatorChar.ToString()))
                            ioswad.Save(wadName);
                        else
                            ioswad.Save(Path.Combine(Path.Combine(Path.Combine(Path.Combine(CURRENT_DIR, "titles"), titleidbox.Text), nusClient.TitleVersion.ToString()), wadName));
                        WriteStatus(String.Format("Patched WAD saved as: {0}", Path.GetFileName(wadName)), infocolor);
                    }
                    catch (Exception ex)
                    {
                        WriteStatus(String.Format("Couldn't save patched WAD: \"{0}\" :(",ex.Message), errorcolor);
                    }
                }
            }

            WriteStatus("NUS Download Finished.");

        }

        void nusClient_Progress(object sender, ProgressChangedEventArgs e)
        {
            dlprogress.Value = e.ProgressPercentage;
        }

        void nusClient_Debug(object sender, libWiiSharp.MessageEventArgs e)
        {
            WriteStatus(e.Message);
        }

        private void NUSDownloader_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WAD_Saveas_Filename = String.Empty;

            SetEnableforDownload(true);
            downloadstartbtn.Text = "Start NUS Download!";
            dlprogress.Value = 0;

            if (IsWin7())
                dlprogress.ShowInTaskbar = false;

            if (script_mode)
                statusbox.Text = "";
        }

        private void consoleCBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (consoleCBox.SelectedIndex == 0)
            {
                // Can pack WADs / Decrypt
                packbox.Enabled = true;
                decryptbox.Enabled = true;
            }
            if (consoleCBox.SelectedIndex == 1)
            {
                // Cannot Pack WADs
                packbox.Checked = false;
                packbox.Enabled = false;

                // Can decrypt if dsikey exists...
                if (dsidecrypt == false)
                {
                    decryptbox.Checked = false;
                    decryptbox.Enabled = false;
                }

                wadnamebox.Enabled = false;
                wadnamebox.Text = "";
            }
        }

        private void packbox_CheckedChanged(object sender, EventArgs e)
        {
            if (packbox.Checked == true)
            {
                wadnamebox.Enabled = true;
                saveaswadbtn.Enabled = true;
                // Change WAD name if applicable
                UpdatePackedName();
            }
            else
            {
                wadnamebox.Enabled = false;
                saveaswadbtn.Enabled = false;
                wadnamebox.Text = String.Empty;
                if (iosPatchCheckbox.Checked)
                    iosPatchCheckbox.Checked = false;
            }
        }

        private void titleidbox_TextChanged(object sender, EventArgs e)
        {
            UpdatePackedName();
            EnablePatchIOSBox();
        }

        private void titleversion_TextChanged(object sender, EventArgs e)
        {
            UpdatePackedName();
        }

        private void EnablePatchIOSBox()
        {
            iosPatchCheckbox.Enabled = TitleIsIOS(titleidbox.Text);
            if (iosPatchCheckbox.Enabled == false)
                iosPatchCheckbox.Checked = false;
        }

        private bool TitleIsIOS(string titleid)
        {
            if (titleid.Length != 16)
                return false;

            if ((titleid == "0000000100000001") || (titleid == "0000000100000002"))
                return false;

            if (titleid.Substring(0, 14) == "00000001000000")
                return true;

            return false;
        }

        /// <summary>
        /// Displays the bytes.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="spacer">What separates the bytes</param>
        /// <returns></returns>
        public string DisplayBytes(byte[] bytes, string spacer)
        {
            string output = "";
            for (int i = 0; i < bytes.Length; ++i)
            {
                output += bytes[i].ToString("X2") + spacer;
            }
            return output;
        }

        private void DatabaseButton_Click(object sender, EventArgs e)
        {
            // Open Database button menu...
            databaseStrip.Show(databaseButton, 2, (2+databaseButton.Height));
        }

        /// <summary>
        /// Clears the database strip.
        /// </summary>
        private void ClearDatabaseStrip()
        {
            object[] thingstoclear = new object[] {
                SystemMenuList, IOSMenuList, WiiWareMenuList, VCMenuList,
                
                // Now Virtual Console
                C64MenuList, NeoGeoMenuList, NESMenuList,
                SNESMenuList, N64MenuList, TurboGrafx16MenuList,
                TurboGrafxCDMenuList, MSXMenuList, SegaMSMenuList,
                GenesisMenuList, VCArcadeMenuList
            };

            foreach (System.Windows.Forms.ToolStripMenuItem tsmiclear in thingstoclear)
            {
                if (tsmiclear.Name != "VCMenuList") // Don't clear the VC Menu...
                    tsmiclear.DropDownItems.Clear();

                if (tsmiclear.OwnerItem != VCMenuList) // and don't disable the VC menu subparts...
                    tsmiclear.Enabled = false;
            }
        }

        /// <summary>
        /// Fills the database strip with the local database.xml file.
        /// </summary>
        private void FillDatabaseStrip(BackgroundWorker worker)
        {
            // Something needs to be done to remove this i guess
            //Control.CheckForIllegalCrossThreadCalls = false;

            Database databaseObj = new Database();
            databaseObj.LoadDatabaseToStream(Path.Combine(CURRENT_DIR, "database.xml"));

            ToolStripMenuItem[] systemItems = databaseObj.LoadSystemTitles();
            for (int a = 0; a < systemItems.Length; a++)
            {
                systemItems[a].DropDownItemClicked += new ToolStripItemClickedEventHandler(DatabaseItem_Clicked);
                for (int b = 0; b < systemItems[a].DropDownItems.Count; b++)
                {
                    ToolStripMenuItem syslowerentry = (ToolStripMenuItem)systemItems[a].DropDownItems[b];
                    if (syslowerentry.DropDownItems.Count > 0)
                    {
                        syslowerentry.DropDownItemClicked += new ToolStripItemClickedEventHandler(DatabaseItem_Clicked);
                    }
                }
                AddToolStripItemToStrip(SystemMenuList, systemItems[a]);
                //SystemMenuList.DropDownItems.Add(systemItems[a]);
            }
            SetPropertyThreadSafe(SystemMenuList, true, "Enabled");
            SetPropertyThreadSafe(SystemMenuList, true, "Visible");

            worker.ReportProgress(25);

            ToolStripMenuItem[] iosItems = databaseObj.LoadIosTitles();
            for (int a = 0; a < iosItems.Length; a++)
            {
                iosItems[a].DropDownItemClicked += new ToolStripItemClickedEventHandler(DatabaseItem_Clicked);
                AddToolStripItemToStrip(IOSMenuList, iosItems[a]);
                //IOSMenuList.DropDownItems.Add(iosItems[a]);
            }
            SetPropertyThreadSafe(IOSMenuList, true, "Enabled");
            SetPropertyThreadSafe(IOSMenuList, true, "Visible");

            worker.ReportProgress(50);

            ToolStripMenuItem[][] vcItems = databaseObj.LoadVirtualConsoleTitles();
            for (int a = 0; a < vcItems.Length; a++)
            {
                for (int b = 0; b < vcItems[a].Length; b++)
			    {
                    vcItems[a][b].DropDownItemClicked += new ToolStripItemClickedEventHandler(DatabaseItem_Clicked);
                    for (int c = 0; c < vcItems[a][b].DropDownItems.Count; c++)
                    {
                        ToolStripMenuItem lowerentry = (ToolStripMenuItem)vcItems[a][b].DropDownItems[c];
                        lowerentry.DropDownItemClicked += new ToolStripItemClickedEventHandler(DatabaseItem_Clicked);
                        
                        //Debug.WriteLine(a + " " + b + " " + c);
                    }
                    try
                    {
                        AddToolStripItemToStrip((ToolStripMenuItem)VCMenuList.DropDownItems[a], vcItems[a][b]);
                    }
                    catch (Exception)
                    {
                        Debug.WriteLine(a + " " + b + "FAIL");
                    }
			    }
            }
            SetPropertyThreadSafe(VCMenuList, true, "Enabled");
            SetPropertyThreadSafe(VCMenuList, true, "Visible");

            worker.ReportProgress(75);

            ToolStripMenuItem[] wwItems = databaseObj.LoadWiiWareTitles();
            for (int a = 0; a < wwItems.Length; a++)
            {
                wwItems[a].DropDownItemClicked += new ToolStripItemClickedEventHandler(DatabaseItem_Clicked);
                for (int b = 0; b < wwItems[a].DropDownItems.Count; b++)
                {
                    ToolStripMenuItem lowerentry = (ToolStripMenuItem)wwItems[a].DropDownItems[b];
                    if (lowerentry.DropDownItems.Count > 0)
                    {
                        lowerentry.DropDownItemClicked += new ToolStripItemClickedEventHandler(DatabaseItem_Clicked);
                    }
                }
                AddToolStripItemToStrip(WiiWareMenuList, wwItems[a]);
                //WiiWareMenuList.DropDownItems.Add(wwItems[a]);
            }
            SetPropertyThreadSafe(WiiWareMenuList, true, "Enabled");
            SetPropertyThreadSafe(WiiWareMenuList, true, "Visible");

            worker.ReportProgress(100);
            /*
            // Load database.xml into memorystream to perhaps reduce disk reads?
            string databasestr = File.ReadAllText(Path.Combine(CURRENT_DIR, "database.xml"));
            System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
            byte[] databasebytes = encoding.GetBytes(databasestr);

            // Load the memory stream
            MemoryStream databaseStream = new MemoryStream(databasebytes);
            databaseStream.Seek(0, SeekOrigin.Begin);

            XmlDocument xDoc = new XmlDocument();
            xDoc.Load(databaseStream);

            // Variables
            string[] XMLNodeTypes = new string[5] {"SYS", "IOS", "VC", "WW", "UPD"};

            int totalLength = xDoc.SelectNodes("/database/*").Count;
            int rnt = 0;
            // Loop through XMLNodeTypes
            for (int i = 0; i < XMLNodeTypes.Length; i++)
            {
                XmlNodeList XMLSpecificNodeTypeList = xDoc.GetElementsByTagName(XMLNodeTypes[i]);

                for (int x = 0; x < XMLSpecificNodeTypeList.Count; x++)
                {
                    ToolStripMenuItem XMLToolStripItem = new ToolStripMenuItem();
                    XmlAttributeCollection XMLAttributes = XMLSpecificNodeTypeList[x].Attributes;

                    string titleID = "";
                    string updateScript;
                    string descname = "";
                    bool dangerous = false;
                    bool ticket = true;

                    // Okay, so now report the progress...
                    rnt = rnt + 1;
                    float currentProgress = ((float) rnt/(float) totalLength)*(float) 100;
                    if (Convert.ToInt16(Math.Round(currentProgress))%10 == 0)
                        worker.ReportProgress(Convert.ToInt16(Math.Round(currentProgress)));

                    // Lol.
                    XmlNodeList ChildrenOfTheNode = XMLSpecificNodeTypeList[x].ChildNodes;

                    for (int z = 0; z < ChildrenOfTheNode.Count; z++)
                    {
                        switch (ChildrenOfTheNode[z].Name)
                        {
                            case "name":
                                descname = ChildrenOfTheNode[z].InnerText;
                                break;
                            case "titleID":
                                titleID = ChildrenOfTheNode[z].InnerText;
                                break;
                            case "titleIDs":
                                updateScript = ChildrenOfTheNode[z].InnerText;
                                XMLToolStripItem.AccessibleDescription = updateScript;
                                    // TODO: Find somewhere better to put this. AND FAST.
                                break;
                            case "version":
                                string[] versions = ChildrenOfTheNode[z].InnerText.Split(',');
                                // Add to region things?
                                if (XMLToolStripItem.DropDownItems.Count > 0)
                                {
                                    for (int b = 0; b < XMLToolStripItem.DropDownItems.Count; b++)
                                    {
                                        if (ChildrenOfTheNode[z].InnerText != "")
                                        {
                                            ToolStripMenuItem regitem =
                                                (ToolStripMenuItem) XMLToolStripItem.DropDownItems[b];
                                            regitem.DropDownItems.Add("Latest Version");
                                            for (int y = 0; y < versions.Length; y++)
                                            {
                                                regitem.DropDownItems.Add("v" + versions[y]);
                                            }
                                            regitem.DropDownItemClicked +=
                                                new ToolStripItemClickedEventHandler(deepitem_clicked);
                                        }
                                    }
                                }
                                else
                                {
                                    XMLToolStripItem.DropDownItems.Add("Latest Version");
                                    if (ChildrenOfTheNode[z].InnerText != "")
                                    {
                                        for (int y = 0; y < versions.Length; y++)
                                        {
                                            XMLToolStripItem.DropDownItems.Add("v" + versions[y]);
                                        }
                                    }
                                }
                                break;
                            case "region":
                                string[] regions = ChildrenOfTheNode[z].InnerText.Split(',');
                                if (ChildrenOfTheNode[z].InnerText != "")
                                {
                                    for (int y = 0; y < regions.Length; y++)
                                    {
                                        XMLToolStripItem.DropDownItems.Add(RegionFromIndex(Convert.ToInt32(regions[y]),
                                                                                           xDoc));
                                    }
                                }
                                break;
                            default:
                                break;
                            case "ticket":
                                ticket = Convert.ToBoolean(ChildrenOfTheNode[z].InnerText);
                                break;
                            case "danger":
                                dangerous = true;
                                XMLToolStripItem.ToolTipText = ChildrenOfTheNode[z].InnerText;
                                break;
                        }
                        XMLToolStripItem.Image = SelectItemImage(ticket, dangerous);

                        if (titleID != "")
                        {
                            XMLToolStripItem.Text = String.Format("{0} - {1}", titleID, descname);
                        }
                        else
                        {
                            XMLToolStripItem.Text = descname;
                        }
                    }
                    AddToolStripItemToStrip(i, XMLToolStripItem, XMLAttributes);
                }
                // Now enable the specific toolbar
                switch (XMLNodeTypes[i])
                {
                    case "IOS":
                        //IOSMenuList.Enabled = true;
                        SetPropertyThreadSafe(IOSMenuList, true, "Enabled");
                        break;
                    case "SYS":
                        
                        break;
                    case "VC":
                        SetPropertyThreadSafe(VCMenuList, true, "Enabled");
                        break;
                    case "WW":
                        SetPropertyThreadSafe(WiiWareMenuList, true, "Enabled");
                        break;
                }
            }*/
        }

        /// <summary>
        /// Adds the tool strip item to strip.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="additionitem">The additionitem.</param>
        /// <param name="attributes">The attributes.</param>
        private void AddToolStripItemToStrip(ToolStripMenuItem menulist, ToolStripMenuItem additionitem)
        {
            Debug.WriteLine(String.Format("Adding item"));
             
            if (this.InvokeRequired)
            {
                Debug.WriteLine("InvokeRequired...");
                AddToolStripItemToStripCallback atsitsc = new AddToolStripItemToStripCallback(AddToolStripItemToStrip);
                this.Invoke(atsitsc, new object[] {menulist, additionitem});
                return;
            }

            menulist.DropDownItems.Add(additionitem);

            /*
            // Deal with VC list depth...
            if (type == 2)
            {
                Debug.WriteLine("Adding:");
                Debug.WriteLine(additionitem);
                switch (attributes[0].Value)
                {
                    case "C64":
                        C64MenuList.DropDownItems.Add(additionitem);
                        break;
                    case "NEO":
                        NeoGeoMenuList.DropDownItems.Add(additionitem);
                        break;
                    case "NES":
                        NESMenuList.DropDownItems.Add(additionitem);
                        break;
                    case "SNES":
                        SNESMenuList.DropDownItems.Add(additionitem);
                        break;
                    case "N64":
                        N64MenuList.DropDownItems.Add(additionitem);
                        break;
                    case "TG16":
                        TurboGrafx16MenuList.DropDownItems.Add(additionitem);
                        break;
                    case "TGCD":
                        TurboGrafxCDMenuList.DropDownItems.Add(additionitem);
                        break;
                    case "MSX":
                        MSXMenuList.DropDownItems.Add(additionitem);
                        break;
                    case "SMS":
                        SegaMSMenuList.DropDownItems.Add(additionitem);
                        break;
                    case "GEN":
                        GenesisMenuList.DropDownItems.Add(additionitem);
                        break;
                    case "ARC":
                        VCArcadeMenuList.DropDownItems.Add(additionitem);
                        break;
                    default:
                        break;
                }
                additionitem.DropDownItemClicked += new ToolStripItemClickedEventHandler(wwitem_regionclicked);
            }
            else if (type == 4)
            {
                // I am a brand new combine harvester
                //MassUpdateList.DropDownItems.Add(additionitem);
                switch (attributes[0].Value)
                {
                    case "KOR":
                        KoreaMassUpdate.DropDownItems.Add(additionitem);
                        KoreaMassUpdate.Enabled = true;
                        break;
                    case "PAL":
                        PALMassUpdate.DropDownItems.Add(additionitem);
                        PALMassUpdate.Enabled = true;
                        break;
                    case "NTSC":
                        NTSCMassUpdate.DropDownItems.Add(additionitem);
                        NTSCMassUpdate.Enabled = true;
                        break;
                    default:
                        Debug.WriteLine("Oops - database error");
                        return;
                }
            }
            else
            {
                // Add SYS, IOS, WW items
                // I thought using index would work in .Items, but I 
                // guess this switch will have to do...
                switch (type)
                {
                    case 0:
                        SystemMenuList.DropDownItems.Add(additionitem);
                        break;
                    case 1:
                        IOSMenuList.DropDownItems.Add(additionitem);
                        break;
                    case 3:
                        WiiWareMenuList.DropDownItems.Add(additionitem);
                        break;
                }
                additionitem.DropDownItemClicked += new ToolStripItemClickedEventHandler(sysitem_versionclicked);
            }*/
        }

        /// <summary>
        /// Mods WAD names to be official.
        /// </summary>
        /// <param name="titlename">The titlename.</param>
        public string OfficialWADNaming(string titlename)
        {
            if (titlename == "MIOS")
                titlename = "RVL-mios-[v].wad";
            else if (titlename.Contains("IOS"))
                titlename = titlename + "-64-[v].wad";
            else if (titlename.Contains("System Menu"))
                titlename = "RVL-WiiSystemmenu-[v].wad";
            else if (titlename.Contains("System Menu"))
                titlename = "RVL-WiiSystemmenu-[v].wad";
            else if (titlename == "BC")
                titlename = "RVL-bc-[v].wad";
            else if (titlename.Contains("Mii Channel"))
                titlename = "RVL-NigaoeNR-[v].wad";
            else if (titlename.Contains("Shopping Channel"))
                titlename = "RVL-Shopping-[v].wad";
            else if (titlename.Contains("Weather Channel"))
                titlename = "RVL-Weather-[v].wad";
            else
                titlename = titlename + "-NUS-[v].wad";

            if (wadnamebox.InvokeRequired)
            {
                OfficialWADNamingCallback ownc = new OfficialWADNamingCallback(OfficialWADNaming);
                wadnamebox.Invoke(ownc, new object[] { titlename });
                return titlename;
            }

            wadnamebox.Text = titlename;

            if (titleversion.Text != "")
                wadnamebox.Text = wadnamebox.Text.Replace("[v]", "v" + titleversion.Text);

            return titlename;
        }

        private void upditem_itemclicked(object sender, ToolStripItemClickedEventArgs e)
        {
            WriteStatus("Preparing to run download script...");
            script_mode = true;
            SetTextThreadSafe(statusbox, "");
            WriteStatus("Starting script download. Please be patient!");
            string[] NUS_Entries = e.ClickedItem.AccessibleDescription.Split('\n');
                // TODO: Find somewhere better to put this. AND FAST!
            for (int i = 0; i < NUS_Entries.Length; i++)
            {
                WriteStatus(NUS_Entries[i]);
            }
            script_filename = "\000";
            nusentries = NUS_Entries;
            BackgroundWorker scripter = new BackgroundWorker();
            scripter.DoWork += new DoWorkEventHandler(RunScript);
            scripter.RunWorkerAsync();
        }

        public void DatabaseItem_Clicked(object sender, ToolStripItemClickedEventArgs e)
        {
            Regex IdandTitle = new Regex(@"[0-9A-Z]*\s-\s.*");
            Regex RegionEntry = new Regex(@"[0-9A-Z][0-9A-Z] \(.*\)");
            Regex VersionEntry = new Regex(@"v[0-9]*.*");

            // This item is a Titleid - Descname entry
            if (IdandTitle.IsMatch(e.ClickedItem.Text))
            {
                string text = e.ClickedItem.Text.Replace(" - ", "~");
                string[] values = text.Split('~');
                titleidbox.Text = values[0];
                statusbox.Text = String.Format(" --- {0} ---", values[1]);
                titleversion.Text = String.Empty;

                if ((e.ClickedItem.Image) == (Database.orange) || (e.ClickedItem.Image) == (Database.redorange))
                {
                    WriteStatus("Note: This title has no ticket and cannot be packed/decrypted!");
                    packbox.Checked = false;
                    decryptbox.Checked = false;
                }

                // Check for danger item
                if ((e.ClickedItem.Image) == (Database.redgreen) || (e.ClickedItem.Image) == (Database.redorange))
                    WriteStatus("\n" + e.ClickedItem.ToolTipText);
            }

            // Region ClickedItem
            if (RegionEntry.IsMatch(e.ClickedItem.Text))
            {
                string text = e.ClickedItem.OwnerItem.Text.Replace(" - ", "~");
                string[] values = text.Split('~');
                titleidbox.Text = values[0];
                statusbox.Text = String.Format(" --- {0} ---", values[1]);
                titleversion.Text = String.Empty;

                // Put 'XX' into title ID
                titleidbox.Text = titleidbox.Text.Replace("XX", e.ClickedItem.Text.Substring(0, 2));

                if ((e.ClickedItem.OwnerItem.Image) == (Database.orange) || (e.ClickedItem.OwnerItem.Image) == (Database.redorange))
                {
                    WriteStatus("Note: This title has no ticket and cannot be packed/decrypted!");
                    packbox.Checked = false;
                    decryptbox.Checked = false;
                }

                // Check for danger item
                if ((e.ClickedItem.OwnerItem.Image) == (Database.redgreen) || (e.ClickedItem.OwnerItem.Image) == (Database.redorange))
                    WriteStatus("\n" + e.ClickedItem.OwnerItem.ToolTipText);
            }

            // Version ClickedItem
            if (VersionEntry.IsMatch(e.ClickedItem.Text) || e.ClickedItem.Text == "Latest Version")
            {
                if (RegionEntry.IsMatch(e.ClickedItem.OwnerItem.Text))
                {
                    string text = e.ClickedItem.OwnerItem.OwnerItem.Text.Replace(" - ", "~");
                    string[] values = text.Split('~');
                    titleidbox.Text = values[0];
                    statusbox.Text = String.Format(" --- {0} ---", values[1]);

                    // Put 'XX' into title ID
                    titleidbox.Text = titleidbox.Text.Replace("XX", e.ClickedItem.OwnerItem.Text.Substring(0, 2));
                }
                else
                { 
                    string text = e.ClickedItem.OwnerItem.Text.Replace(" - ", "~");
                    string[] values = text.Split('~');
                    titleidbox.Text = values[0];
                    statusbox.Text = String.Format(" --- {0} ---", values[1]);
                }

                // Set version
                if (e.ClickedItem.Text == "Latest Version")
                    titleversion.Text = String.Empty;
                else
                {
                    string[] version = e.ClickedItem.Text.Replace("v", "").Split(' ');
                    titleversion.Text = version[0];
                }

                if (RegionEntry.IsMatch(e.ClickedItem.OwnerItem.Text))
                {
                    if ((e.ClickedItem.OwnerItem.OwnerItem.Image) == (Database.orange) || (e.ClickedItem.OwnerItem.OwnerItem.Image) == (Database.redorange))
                    {
                        WriteStatus("Note: This title has no ticket and cannot be packed/decrypted!");
                        packbox.Checked = false;
                        decryptbox.Checked = false;
                    }

                    // Check for danger item
                    if ((e.ClickedItem.OwnerItem.OwnerItem.Image) == (Database.redgreen) || (e.ClickedItem.OwnerItem.OwnerItem.Image) == (Database.redorange))
                        WriteStatus("\n" + e.ClickedItem.OwnerItem.OwnerItem.ToolTipText);
                }
                else
                {
                    if ((e.ClickedItem.OwnerItem.Image) == (Database.orange) || (e.ClickedItem.OwnerItem.Image) == (Database.redorange))
                    {
                        WriteStatus("Note: This title has no ticket and cannot be packed/decrypted!");
                        packbox.Checked = false;
                        decryptbox.Checked = false;
                    }

                    // Check for danger item
                    if ((e.ClickedItem.OwnerItem.Image) == (Database.redgreen) || (e.ClickedItem.OwnerItem.Image) == (Database.redorange))
                        WriteStatus("\n" + e.ClickedItem.OwnerItem.ToolTipText);
                }
            }
        }

        /// <summary>
        /// Gathers the region based on index
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="databasexml">XmlDocument with database inside</param>
        /// <returns>Region desc</returns>
        private string RegionFromIndex(int index, XmlDocument databasexml)
        {
            /* Typical Region XML
             * <REGIONS>
		            <region index="0">41 (All/System)</region>
		            <region index=1>44 (German)</region>
		            <region index=2>45 (USA/NTSC)</region>
		            <region index=3>46 (French)</region>
		            <region index=4>4A (Japan)</region>
		            <region index=5>4B (Korea)</region>
		            <region index=6>4C (Japanese Import to Europe/Australia/PAL)</region>
		            <region index=7>4D (American Import to Europe/Australia/PAL)</region>
		            <region index=8>4E (Japanese Import to USA/NTSC)</region>
		            <region index=9>50 (Europe/PAL)</region>
		            <region index=10>51 (Korea w/ Japanese Language)</region>
		            <region index=11>54 (Korea w/ English Language)</region>
		            <region index=12>58 (Some Homebrew)</region>
	           </REGIONS>
            */

            XmlNodeList XMLRegionList = databasexml.GetElementsByTagName("REGIONS");
            XmlNodeList ChildrenOfTheNode = XMLRegionList[0].ChildNodes;

            // For each child node (region node)
            for (int z = 0; z < ChildrenOfTheNode.Count; z++)
            {
                // Gather attributes (index='x')
                XmlAttributeCollection XMLAttributes = ChildrenOfTheNode[z].Attributes;

                // Return value of node if index matches
                if (Convert.ToInt32(XMLAttributes[0].Value) == index)
                    return ChildrenOfTheNode[z].InnerText;
            }

            return "XX (Error)";
        }
        
        /// <summary>
        /// Loads the region codes.
        /// </summary>
        private void LoadRegionCodes()
        {
            // TODO: make this check InvokeRequired...
            if (this.InvokeRequired)
            {
                BootChecksCallback bcc = new BootChecksCallback(LoadRegionCodes);
                this.Invoke(bcc);
                return;
            }

            Database databaseObj = new Database();
            databaseObj.LoadDatabaseToStream(Path.Combine(CURRENT_DIR, "database.xml"));

            ToolStripMenuItem[] regionItems = databaseObj.LoadRegionCodes();

            // For each child node (region node)
            for (int z = 0; z < regionItems.Length; z++)
            {
                RegionCodesList.DropDownItems.Add(regionItems[z].Text);
            }
        } 

        private void RegionCodesList_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (titleidbox.Text.Length == 16)
                titleidbox.Text = titleidbox.Text.Substring(0, 14) + e.ClickedItem.Text.Substring(0, 2);
        }

        /// <summary>
        /// Removes the illegal characters.
        /// </summary>
        /// <param name="databasestr">removes the illegal chars</param>
        /// <returns>legal string</returns>
        private static string RemoveIllegalCharacters(string databasestr)
        {
            // Database strings must contain filename-legal characters.
            foreach (char illegalchar in System.IO.Path.GetInvalidFileNameChars())
            {
                if (databasestr.Contains(illegalchar.ToString()))
                    databasestr = databasestr.Replace(illegalchar, '-');
            }
            return databasestr;
        }

        private void ClearStatusbox(object sender, EventArgs e)
        {
            // Clear Statusbox.text
            statusbox.Text = "";
        }

        /// <summary>
        /// Makes everything disabled/enabled.
        /// </summary>
        /// <param name="enabled">if set to <c>true</c> [enabled].</param>
        private void SetEnableforDownload(bool enabled)
        {
            if (this.InvokeRequired)
            {
                SetEnableForDownloadCallback sefdcb = new SetEnableForDownloadCallback(SetEnableforDownload);
                this.Invoke(sefdcb, new object[] { enabled });
                return;
            }
            // Disable things the user should not mess with during download...
            downloadstartbtn.Enabled = enabled;
            titleidbox.Enabled = enabled;
            titleversion.Enabled = enabled;
            Extrasbtn.Enabled = enabled;
            databaseButton.Enabled = enabled;
            packbox.Enabled = enabled;
            localuse.Enabled = enabled;
            saveaswadbtn.Enabled = enabled;
            decryptbox.Enabled = enabled;
            keepenccontents.Enabled = enabled;
            scriptsbutton.Enabled = enabled;
            consoleCBox.Enabled = enabled;
            iosPatchCheckbox.Enabled = enabled;
        }

        /// <summary>
        /// Makes tooltips disappear in the database, as many contain danger tag info.
        /// </summary>
        /// <param name="enabled">if set to <c>true</c> [enabled].</param>
        private void ShowInnerToolTips(bool enabled)
        {
            // Force tooltips to GTFO in sub menus...
            foreach (ToolStripItem item in databaseStrip.Items)
            {
                try
                {
                    ToolStripMenuItem menuitem = (ToolStripMenuItem) item;
                    menuitem.DropDown.ShowItemToolTips = false;
                }
                catch (Exception)
                {
                    // Do nothing, some objects will not cast.
                }
            }
            foreach (ToolStripItem item in scriptsStrip.Items)
            {
                try
                {
                    ToolStripMenuItem menuitem = (ToolStripMenuItem)item;
                    menuitem.DropDown.ShowItemToolTips = false;
                }
                catch (Exception)
                {
                    // Do nothing, some objects will not cast.
                }
            }
        }

        /// <summary>
        /// Selects the database item image.
        /// </summary>
        /// <param name="ticket">if set to <c>true</c> [ticket].</param>
        /// <param name="danger">if set to <c>true</c> [danger].</param>
        /// <returns>Correct Image</returns>
        private System.Drawing.Image SelectItemImage(bool ticket, bool danger)
        {
            // All is good, go green...
            if ((ticket) && (!danger))
                return green;

            // There's no ticket, but danger is clear...
            if ((!ticket) && (!danger))
                return orange;

            // DANGER WILL ROBINSON...
            if ((ticket) && (danger))
                return redgreen;

            // Double bad...
            if ((!ticket) && (danger))
                return redorange;

            return null;
        }

        /// <summary>
        /// Updates the name of the packed WAD in the textbox.
        /// </summary>
        private void UpdatePackedName()
        {
            // Change WAD name if applicable

            string title_name = null;

            if ((titleidbox.Enabled == true || script_mode == true) && (packbox.Checked == true))
            {
                if (titleversion.Text != "")
                {
                    wadnamebox.Text = titleidbox.Text + "-NUS-v" + titleversion.Text + ".wad";
                }
                else
                {
                    wadnamebox.Text = titleidbox.Text + "-NUS-[v]" + titleversion.Text + ".wad";
                }

                if ((File.Exists("database.xml") == true) && (titleidbox.Text.Length == 16))
                    title_name = NameFromDatabase(titleidbox.Text);

                if (title_name != null)
                {
                    wadnamebox.Text = wadnamebox.Text.Replace(titleidbox.Text, title_name);
                    OfficialWADNaming(title_name);
                }
            }
            wadnamebox.Text = RemoveIllegalCharacters(wadnamebox.Text);
        }

        /// <summary>
        /// Determines whether OS is win7.
        /// </summary>
        /// <returns>
        /// 	<c>true</c> if OS = win7; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsWin7()
        {
            return (Environment.OSVersion.VersionString.Contains("6.1") == true);
        }

        private byte[] NewIntegertoByteArray(int theInt, int arrayLen)
        {
            byte[] resultArray = new byte[arrayLen];

            for (int i = arrayLen - 1; i >= 0; i--)
            {
                resultArray[i] = (byte) ((theInt >> (8*i)) & 0xFF);
            }
            Array.Reverse(resultArray);

            // Fix duplication, rewrite extra to 0x00;
            if (arrayLen > 4)
            {
                for (int i = 0; i < (arrayLen - 4); i++)
                {
                    resultArray[i] = 0x00;
                }
            }
            return resultArray;
        }

        private WebClient ConfigureWithProxy(WebClient client)
        {
                // Proxy
                if (!(String.IsNullOrEmpty(proxy_url)))
                {
                    WebProxy customproxy = new WebProxy();
                    customproxy.Address = new Uri(proxy_url);
                    if (String.IsNullOrEmpty(proxy_usr))
                        customproxy.UseDefaultCredentials = true;
                    else
                    {
                        NetworkCredential cred = new NetworkCredential();
                        cred.UserName = proxy_usr;

                        if (!(String.IsNullOrEmpty(proxy_pwd)))
                            cred.Password = proxy_pwd;

                        customproxy.Credentials = cred;
                    }
                    client.Proxy = customproxy;
                    WriteStatus("  - Custom proxy settings applied!");
                }
                else
                {
                    try
                    {
                    client.Proxy = WebRequest.GetSystemWebProxy();
                    client.UseDefaultCredentials = true;
                    }
                    catch (NotImplementedException)
                    {
                        // Linux support
                        WriteStatus("This operating system does not support automatic system proxy usage. Operating without a proxy...");
                    }
                }
            return client;
        }

        /// <summary>
        /// Retrieves the new database via WiiBrew.
        /// </summary>
        /// <returns>Database as a String</returns>
        private void RetrieveNewDatabase(object sender, DoWorkEventArgs e)
        {
            // Retrieve Wiibrew database page source code
            WebClient databasedl = new WebClient();

            // Proxy
            databasedl = ConfigureWithProxy(databasedl);

            string wiibrewsource =
                databasedl.DownloadString("http://www.wiibrew.org/wiki/NUS_Downloader/database?cachesmash=" +
                                          System.DateTime.Now.ToString());

            // Strip out HTML
            wiibrewsource = Regex.Replace(wiibrewsource, @"<(.|\n)*?>", "");

            // Shrink to fix only the database
            string startofdatabase = "&lt;database v";
            string endofdatabase = "&lt;/database&gt;";
            wiibrewsource = wiibrewsource.Substring(wiibrewsource.IndexOf(startofdatabase),
                                                    wiibrewsource.Length - wiibrewsource.IndexOf(startofdatabase));
            wiibrewsource = wiibrewsource.Substring(0, wiibrewsource.IndexOf(endofdatabase) + endofdatabase.Length);

            // Fix ", <, >, and spaces
            wiibrewsource = wiibrewsource.Replace("&lt;", "<");
            wiibrewsource = wiibrewsource.Replace("&gt;", ">");
            wiibrewsource = wiibrewsource.Replace("&quot;", '"'.ToString());
            wiibrewsource = wiibrewsource.Replace("&nbsp;", " "); // Shouldn't occur, but they happen...

            // Return parsed xml database...
            e.Result = wiibrewsource;
        }

        private void RetrieveNewDatabase_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            string database = e.Result.ToString();
            try
            {
                Database db = new Database();
                db.LoadDatabaseToStream(Path.Combine(CURRENT_DIR, "database.xml"));
                string currentversion = db.GetDatabaseVersion();
                string onlineversion = Database.GetDatabaseVersion(database);
                WriteStatus(" - Database successfully parsed!");
                WriteStatus("   - Current Database Version: " + currentversion);
                WriteStatus("   - Online Database Version: " + onlineversion);

                if (currentversion == onlineversion)
                {
                    WriteStatus(" - You have the latest database version!");
                    return;
                }
            }
            catch (FileNotFoundException)
            {
                WriteStatus(" - Database does not yet exist.");
                WriteStatus("   - Online Database Version: " + Database.GetDatabaseVersion(database));
            }

            bool isCreation = false;
            if (File.Exists("database.xml"))
            {
                WriteStatus(" - Overwriting your current database.xml...");
                WriteStatus(" - The old database will become 'olddatabase.xml' in case the new one is faulty.");

                string olddatabase = File.ReadAllText("database.xml");
                File.WriteAllText("olddatabase.xml", olddatabase);
                File.Delete("database.xml");
                File.WriteAllText("database.xml", database);
            }
            else
            {
                WriteStatus(" - database.xml has been created.");
                File.WriteAllText("database.xml", database);
                isCreation = true;
            }

            // Load it up...
            this.fds.RunWorkerAsync();

            if (isCreation)
            {
                WriteStatus("Database successfully created!");
                databaseButton.Visible = true;
                //databaseButton.Enabled = false;
                updateDatabaseToolStripMenuItem.Text = "Download Database";
            }
            else
            {
                WriteStatus("Database successfully updated!");
            }
        }

        private void updateDatabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            statusbox.Text = "";
            WriteStatus("Updating your database.xml from Wiibrew.org");

            BackgroundWorker dbFetcher = new BackgroundWorker();
            dbFetcher.DoWork += new DoWorkEventHandler(RetrieveNewDatabase);
            dbFetcher.RunWorkerCompleted += new RunWorkerCompletedEventHandler(RetrieveNewDatabase_Completed);
            dbFetcher.RunWorkerAsync();
        }

        private void loadInfoFromTMDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Extras menu -> Load TMD...
            LoadTitleFromTMD();
        }

        /// <summary>
        /// Sends the SOAP request to NUS.
        /// </summary>
        /// <param name="soap_xml">The Request</param>
        /// <returns></returns>
        public string SendSOAPRequest(string soap_xml)
        {
            System.Net.HttpWebRequest req =
                (System.Net.HttpWebRequest)
                System.Net.HttpWebRequest.Create("http://nus.shop.wii.com:80/nus/services/NetUpdateSOAP");
            req.Method = "POST";
            req.UserAgent = "wii libnup/1.0";
            req.Headers.Add("SOAPAction", '"' + "urn:nus.wsapi.broadon.com/" + '"');

            // Proxy
            if (!(String.IsNullOrEmpty(proxy_url)))
            {
                WebProxy customproxy = new WebProxy();
                customproxy.Address = new Uri(proxy_url);
                if (String.IsNullOrEmpty(proxy_usr))
                    customproxy.UseDefaultCredentials = true;
                else
                {
                    NetworkCredential cred = new NetworkCredential();
                    cred.UserName = proxy_usr;

                    if (!(String.IsNullOrEmpty(proxy_pwd)))
                        cred.Password = proxy_pwd;

                    customproxy.Credentials = cred;
                }
                req.Proxy = customproxy;
                WriteStatus("  - Custom proxy settings applied!");
            }
            else
            {
                req.Proxy = WebRequest.GetSystemWebProxy();
                req.UseDefaultCredentials = true;
            }

            Stream writeStream = req.GetRequestStream();

            UTF8Encoding encoding = new UTF8Encoding();
            byte[] bytes = encoding.GetBytes(soap_xml);
            req.ContentType = "text/xml; charset=utf-8";
            //req.ContentLength = bytes.Length;
            writeStream.Write(bytes, 0, bytes.Length);
            writeStream.Close();
            Application.DoEvents();
            try
            {
                string result;
                System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse) req.GetResponse();

                using (Stream responseStream = resp.GetResponseStream())
                {
                    using (StreamReader readStream = new StreamReader(responseStream, Encoding.UTF8))
                    {
                        result = readStream.ReadToEnd();
                    }
                }
                req.Abort();
                Application.DoEvents();
                return result;
            }
            catch (Exception ex)
            {
                req.Abort();

                return ex.Message.ToString();
            }
        }

        private void emulateUpdate_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            // Begin Wii System Update
            statusbox.Text = "";
            WriteStatus("Starting Wii System Update...");

            scriptsStrip.Close();

            string deviceID = "4362227770";
            string messageID = "13198105123219138";
            string attr = "2";

            string RegionID = e.ClickedItem.Text.Substring(0, 3);
            if (RegionID == "JAP") // Japan fix, only region not w/ 1st 3 letters same as ID.
                RegionID = "JPN";

            string CountryCode = RegionID.Substring(0, 2);

            /* [14:26] <Galaxy|> RegionID: USA, Country: US; 
            RegionID: JPN, Country: JP; 
            RegionID: EUR, Country: EU; 
            RegionID: KOR, Country: KO; */

            string soap_req = "<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\"" +
                              " xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"" +
                              " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n" +
                              "<soapenv:Body>\n<GetSystemUpdateRequest xmlns=\"urn:nus.wsapi.broadon.com\">\n" +
                              "<Version>1.0</Version>\n<MessageId>" + messageID + "</MessageId>\n<DeviceId>" + deviceID +
                              "</DeviceId>\n" +
                              "<RegionId>" + RegionID + "</RegionId>\n<CountryCode>" + CountryCode +
                              "</CountryCode>\n<TitleVersion>\n<TitleId>0000000100000001</TitleId>\n" +
                              "<Version>2</Version>\n</TitleVersion>\n<TitleVersion>\n<TitleId>0000000100000002</TitleId>\n" +
                              "<Version>33</Version>\n</TitleVersion>\n<TitleVersion>\n<TitleId>0000000100000009</TitleId>\n" +
                              "<Version>516</Version>\n</TitleVersion>\n<Attribute>" + attr +
                              "</Attribute>\n<AuditData></AuditData>\n" +
                              "</GetSystemUpdateRequest>\n</soapenv:Body>\n</soapenv:Envelope>";

            WriteStatus(" - Sending SOAP Request to NUS...");
            WriteStatus("   - Region: " + RegionID);
            string update_xml = SendSOAPRequest(soap_req);
            if (update_xml != null)
                WriteStatus("   - Recieved Update Info!");
            else
            {
                WriteStatus("   - Fail.");
                return;
            }
            WriteStatus("   - Title information:");

            string script_text = "";

            XmlDocument xDoc = new XmlDocument();
            xDoc.LoadXml(update_xml);
            XmlNodeList TitleList = xDoc.GetElementsByTagName("TitleVersion");
            for (int a = 0; a < TitleList.Count; a++)
            {
                XmlNodeList TitleInfo = TitleList[a].ChildNodes;
                string TitleID = "";
                string Version = "";

                for (int b = 0; b < TitleInfo.Count; b++)
                {
                    switch (TitleInfo[b].Name)
                    {
                        case "TitleId":
                            TitleID = TitleInfo[b].InnerText;
                            break;
                        case "Version":
                            Version = TitleInfo[b].InnerText;
                            break;
                        default:
                            break;
                    }
                }
                WriteStatus(String.Format("    - {0} [v{1}]", TitleID, Version));

                if ((NUSDFileExists("database.xml") == true) && ((!(String.IsNullOrEmpty(NameFromDatabase(TitleID))))))
                    //statusbox.Text += String.Format(" [{0}]", NameFromDatabase(TitleID));
                    WriteStatus(String.Format(" [{0}]", NameFromDatabase(TitleID)));

                script_text += String.Format("{0} {1}\n", TitleID,
                                             DisplayBytes(NewIntegertoByteArray(Convert.ToInt32(Version), 2), ""));
            }

            WriteStatus(" - Outputting results to NUS script...");

            if (!(Directory.Exists(Path.Combine(CURRENT_DIR, "scripts"))))
            {
                Directory.CreateDirectory(Path.Combine(CURRENT_DIR, "scripts"));
                WriteStatus("  - Created 'scripts\' directory.");
            }
            string time = RemoveIllegalCharacters(DateTime.Now.ToShortTimeString());
            File.WriteAllText(
                String.Format(Path.Combine(CURRENT_DIR, Path.Combine("scripts","{0}_Update_{1}_{2}_{3} at {4}.nus")), RegionID,
                              DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Year, time), script_text);
            WriteStatus(" - Script written!");
            scriptsLocalMenuEntry.Enabled = false;
            this.scriptsWorker.RunWorkerAsync();
            
            WriteStatus(" - Run this script if you feel like downloading the update!");
            // TODO: run the script...
        }

        /// <summary>
        /// Looks for a title's name by TitleID in Database.
        /// </summary>
        /// <param name="titleid">The titleid.</param>
        /// <returns>Existing name; else null</returns>
        private string NameFromDatabase(string titleid)
        {
            // DANGER: BAD h4x HERE!!
            // Fix MIOS/BC naming
            if (titleid == "0000000100000101")
                return "MIOS";
            else if (titleid == "0000000100000100")
                return "BC";

            XmlDocument xDoc = new XmlDocument();
            xDoc.Load("database.xml");

            // Variables
            string[] XMLNodeTypes = new string[4] {"SYS", "IOS", "VC", "WW"};

            // Loop through XMLNodeTypes
            for (int i = 0; i < XMLNodeTypes.Length; i++) // FOR THE FOUR TYPES OF NODES
            {
                XmlNodeList XMLSpecificNodeTypeList = xDoc.GetElementsByTagName(XMLNodeTypes[i]);

                for (int x = 0; x < XMLSpecificNodeTypeList.Count; x++) // FOR EACH ITEM IN THE LIST OF A NODE TYPE
                {
                    bool found_it = false;

                    // Lol.
                    XmlNodeList ChildrenOfTheNode = XMLSpecificNodeTypeList[x].ChildNodes;

                    for (int z = 0; z < ChildrenOfTheNode.Count; z++) // FOR EACH CHILD NODE
                    {
                        switch (ChildrenOfTheNode[z].Name)
                        {
                            case "titleID":
                                if (ChildrenOfTheNode[z].InnerText == titleid)
                                    found_it = true;
                                else if ((ChildrenOfTheNode[z].InnerText.Substring(0, 14) + "XX") ==
                                         (titleid.Substring(0, 14) + "XX") &&
                                         (titleid.Substring(0, 14) != "00000001000000"))
                                    found_it = true;
                                else
                                    found_it = false;
                                break;
                            default:
                                break;
                        }
                    }

                    if (found_it)
                    {
                        for (int z = 0; z < ChildrenOfTheNode.Count; z++) // FOR EACH CHILD NODE
                        {
                            switch (ChildrenOfTheNode[z].Name)
                            {
                                case "name":
                                    return ChildrenOfTheNode[z].InnerText;
                                default:
                                    break;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private void packbox_EnabledChanged(object sender, EventArgs e)
        {
            saveaswadbtn.Enabled = packbox.Enabled;
            //deletecontentsbox.Enabled = packbox.Enabled;
        }

        private void SaveProxyBtn_Click(object sender, EventArgs e)
        {
            if ((String.IsNullOrEmpty(ProxyURL.Text)) && (String.IsNullOrEmpty(ProxyUser.Text)) &&
                ((File.Exists(Path.Combine(CURRENT_DIR, "proxy.txt")))))
            {
                File.Delete(Path.Combine(CURRENT_DIR, "proxy.txt"));
                proxyBox.Visible = false;
                proxy_usr = "";
                proxy_url = "";
                proxy_pwd = "";
                WriteStatus("Proxy settings deleted!");
                return;
            }
            else if ((String.IsNullOrEmpty(ProxyURL.Text)) && (String.IsNullOrEmpty(ProxyUser.Text)) &&
                     ((!(File.Exists(Path.Combine(CURRENT_DIR, "proxy.txt"))))))
            {
                proxyBox.Visible = false;
                WriteStatus("No proxy settings saved!");
                return;
            }

            string proxy_file = "";

            if (!(String.IsNullOrEmpty(ProxyURL.Text)))
            {
                proxy_file += ProxyURL.Text + "\n";
                proxy_url = ProxyURL.Text;
            }

            if (!(String.IsNullOrEmpty(ProxyUser.Text)))
            {
                proxy_file += ProxyUser.Text;
                proxy_usr = ProxyUser.Text;
            }

            if (!(String.IsNullOrEmpty(proxy_file)))
            {
                File.WriteAllText(Path.Combine(CURRENT_DIR, "proxy.txt"), proxy_file);
                WriteStatus("Proxy settings saved!");
            }

            proxyBox.Visible = false;

            SetAllEnabled(false);
            ProxyVerifyBox.Visible = true;
            ProxyVerifyBox.Enabled = true;
            ProxyPwdBox.Enabled = true;
            SaveProxyBtn.Enabled = true;
            ProxyVerifyBox.Select();
        }

        private void proxySettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Check for Proxy Settings file...
            if (File.Exists(Path.Combine(CURRENT_DIR, "proxy.txt")) == true)
            {
                string[] proxy_file = File.ReadAllLines(Path.Combine(CURRENT_DIR, "proxy.txt"));

                ProxyURL.Text = proxy_file[0];
                if (proxy_file.Length > 1)
                {
                    ProxyUser.Text = proxy_file[1];
                }
            }

            proxyBox.Visible = true;
        }

        private void SaveProxyPwdButton_Click(object sender, EventArgs e)
        {
            proxy_pwd = ProxyPwdBox.Text;
            ProxyVerifyBox.Visible = false;
            SetAllEnabled(true);
        }

        private void ProxyPwdBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == Convert.ToChar(Keys.Enter))
                SaveProxyPwdButton_Click("LOLWUT", EventArgs.Empty);
        }

        private void ProxyAssistBtn_Click(object sender, EventArgs e)
        {
            MessageBox.Show("If you are behind a proxy, set these settings to get through to NUS." +
                            " If you have an alternate port for accessing your proxy, add ':' followed by the port.");
        }

        private void loadNUSScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Open a NUS script.
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = false;
            ofd.Filter = "NUS Scripts|*.nus|All Files|*.*";
            if (Directory.Exists(Path.Combine(CURRENT_DIR, "scripts")))
                ofd.InitialDirectory = Path.Combine(CURRENT_DIR, "scripts");
            ofd.Title = "Load a NUS/Wiimpersonator script.";

            if (ofd.ShowDialog() != DialogResult.Cancel)
            {
                script_filename = ofd.FileName;
                BackgroundWorker scripter = new BackgroundWorker();
                scripter.DoWork += new DoWorkEventHandler(RunScript);
                scripter.RunWorkerAsync();
            }
        }

        /// <summary>
        /// Runs a NUS script (BG).
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.ComponentModel.DoWorkEventArgs"/> instance containing the event data.</param>
        private void RunScript(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            script_mode = true;
            SetTextThreadSafe(statusbox, "");
            WriteStatus("Starting script download. Please be patient!");
            if (!File.Exists(Path.Combine(CURRENT_DIR, "output_" + Path.GetFileNameWithoutExtension(script_filename))))
                Directory.CreateDirectory(Path.Combine(CURRENT_DIR, "output_" + Path.GetFileNameWithoutExtension(script_filename)));
            string[] NUS_Entries;
            if (script_filename != "\000")
            {
                NUS_Entries = File.ReadAllLines(script_filename);
            }
            else
            {
                NUS_Entries = nusentries;
            }
            WriteStatus(String.Format(" - Script loaded ({0} Titles)", NUS_Entries.Length));

            for (int a = 0; a < NUS_Entries.Length; a++)
            {
                // Download the title
                WriteStatus(String.Format("===== Running Download ({0}/{1}) =====", a + 1, NUS_Entries.Length));
                string[] title_info = NUS_Entries[a].Split(' ');
                // don't let the delete issue reappear...
                if (string.IsNullOrEmpty(title_info[0]))
                    break;

                // WebClient configuration
                WebClient nusWC = new WebClient();
                nusWC = ConfigureWithProxy(nusWC);
                nusWC.Headers.Add("User-Agent", "wii libnup/1.0"); // Set UserAgent to Wii value

                // Create\Configure NusClient
                libWiiSharp.NusClient nusClient = new libWiiSharp.NusClient();
                nusClient.ConfigureNusClient(nusWC);
                nusClient.UseLocalFiles = localuse.Checked;
                nusClient.ContinueWithoutTicket = true;
                nusClient.Debug += new EventHandler<libWiiSharp.MessageEventArgs>(nusClient_Debug);

                libWiiSharp.StoreType[] storeTypes = new libWiiSharp.StoreType[1];
                // There's no harm in outputting everything i suppose
                storeTypes[0] = libWiiSharp.StoreType.All;

                int title_version = int.Parse(title_info[1], System.Globalization.NumberStyles.HexNumber);

                string wadName = NameFromDatabase(title_info[0]);
                if (wadName != null)
                    wadName = OfficialWADNaming(wadName);
                else
                    wadName = title_info[0] + "-NUS-v" + title_version + ".wad";

                nusClient.DownloadTitle(title_info[0], title_version.ToString(), Path.Combine(CURRENT_DIR, ("output_" + Path.GetFileNameWithoutExtension(script_filename))), wadName, storeTypes);

                /*
                SetTextThreadSafe(titleidbox, title_info[0]);
                SetTextThreadSafe(titleversion,
                    Convert.ToString(256*
                                     (byte.Parse(title_info[1].Substring(0, 2),
                                                 System.Globalization.NumberStyles.HexNumber))));
                SetTextThreadSafe(titleversion,
                    Convert.ToString(Convert.ToInt32(titleversion.Text) +
                                     byte.Parse(title_info[1].Substring(2, 2),
                                                System.Globalization.NumberStyles.HexNumber)));

                button3_Click("Scripter", EventArgs.Empty);

                Thread.Sleep(1000);

                while (NUSDownloader.IsBusy)
                {
                    Thread.Sleep(1000);
                } */
            }
            script_mode = false;
            WriteStatus("Script completed!");
        }

        private void scriptsbutton_Click(object sender, EventArgs e)
        {
            // Show scripts menu
            scriptsStrip.Show(scriptsbutton, 2, (2+scriptsbutton.Height));
        }

        private void DatabaseEnabled(bool enabled)
        {
            for (int a = 0; a < databaseStrip.Items.Count; a++)
            {
                databaseStrip.Items[a].Enabled = enabled;
                databaseStrip.Items[a].Visible = enabled;
            }

            for (int b = 0; b < VCMenuList.DropDownItems.Count; b++)
            {
                VCMenuList.DropDownItems[b].Enabled = true;
                VCMenuList.DropDownItems[b].Visible = true;
            }
        }

        void scriptsWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            scriptsLocalMenuEntry.Enabled = true;
        }

        void OrganizeScripts(object sender, DoWorkEventArgs e)
        {
            //throw new NotImplementedException();

            if (Directory.Exists(Path.Combine(CURRENT_DIR, "scripts")) == false)
            {
                WriteStatus("Scripts directory not found...");
                WriteStatus("- Creating it.");
                Directory.CreateDirectory(Path.Combine(CURRENT_DIR, "scripts"));
            }

            // Clear any entries from previous runthrough
            if (scriptsLocalMenuEntry.DropDownItems.Count > 0)
            {
                // TODO: i suppose this is bad amiright
                Control.CheckForIllegalCrossThreadCalls = false;
                scriptsLocalMenuEntry.DropDownItems.Clear();
            }
            

            // Add directories w/ scripts in \scripts\
            foreach (string directory in Directory.GetDirectories(Path.Combine(CURRENT_DIR, "scripts"), "*", SearchOption.TopDirectoryOnly))
            {
                if (Directory.GetFiles(directory, "*.nus", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    DirectoryInfo dinfo = new DirectoryInfo(directory);
                    ToolStripMenuItem folder_item = new ToolStripMenuItem();
                    folder_item.Text = dinfo.Name + Path.DirectorySeparatorChar;
                    folder_item.Image = Properties.Resources.folder_table;


                    foreach (string nusscript in Directory.GetFiles(directory, "*.nus", SearchOption.TopDirectoryOnly))
                    {
                        FileInfo finfo = new FileInfo(nusscript);
                        ToolStripMenuItem nus_script_item = new ToolStripMenuItem();
                        nus_script_item.Text = finfo.Name;
                        nus_script_item.Image = Properties.Resources.script_start;
                        folder_item.DropDownItems.Add(nus_script_item);

                            nus_script_item.Click += new EventHandler(nus_script_item_Click);
                        }

                        scriptsLocalMenuEntry.DropDownItems.Add(folder_item);
                    }
            }

            // Add scripts in \scripts\
            foreach (string nusscript in Directory.GetFiles(Path.Combine(CURRENT_DIR, "scripts"), "*.nus", SearchOption.TopDirectoryOnly))
            {
                FileInfo finfo = new FileInfo(nusscript);
                ToolStripMenuItem nus_script_item = new ToolStripMenuItem();
                nus_script_item.Text = finfo.Name;
                nus_script_item.Image = Properties.Resources.script_start;
                scriptsLocalMenuEntry.DropDownItems.Add(nus_script_item);

                nus_script_item.Click += new EventHandler(nus_script_item_Click);
            }
        }

        private void aboutNUSDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Display About Text...
            statusbox.Text = "";
            WriteStatus("NUS Downloader (NUSD)");
            WriteStatus("You are running version: " + version);
            if (version.StartsWith("SVN"))
                WriteStatus("SVN BUILD: DO NOT REPORT BROKEN FEATURES!");

            WriteStatus("This application created by WB3000");
            WriteStatus("Various sections contributed by lukegb");
            WriteStatus(String.Empty);
            /*
            if (NUSDFileExists("key.bin") == false)
                WriteStatus("Wii Decryption: Need (key.bin)");
            else
                WriteStatus("Wii Decryption: OK");

            if (NUSDFileExists("kkey.bin") == false)
                WriteStatus("Wii Korea Decryption: Need (kkey.bin)");
            else
                WriteStatus("Wii Korea Decryption: OK");
            */
            if (NUSDFileExists("dsikey.bin") == false)
                WriteStatus("DSi Decryption: Need (dsikey.bin)");
            else
                WriteStatus("DSi Decryption: OK");

            if (NUSDFileExists("database.xml") == false)
                WriteStatus("Database: Need (database.xml)");
            else
                WriteStatus("Database: OK");

            if (IsWin7())
                WriteStatus("Windows 7 Features: Enabled");

            WriteStatus("");
            WriteStatus("Special thanks to:");
            WriteStatus(" * Crediar for his wadmaker tool + source, and for the advice!");
            WriteStatus(" * Leathl for libWiiSharp.");
            WriteStatus(" * SquidMan/Galaxy/comex/Xuzz for advice/sources.");
            WriteStatus(" * Pasta for database compilation assistance.");
            WriteStatus(" * Napo7 for testing proxy usage.");
            WriteStatus(" * Wyatt O'Day for the Windows7ProgressBar Control.");
            WriteStatus(" * Famfamfam for the Silk Icon Set.");
            WriteStatus(" * #WiiDev for answering the tough questions.");
            WriteStatus(" * Anyone who helped beta test!");
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            SaveProxyPwdPermanentBtn.Enabled = checkBox1.Checked;
        }

        private void SaveProxyPwdPermanentBtn_Click(object sender, EventArgs e)
        {
            proxy_pwd = ProxyPwdBox.Text;

            string proxy_file = File.ReadAllText(Path.Combine(CURRENT_DIR, "proxy.txt"));

            proxy_file += String.Format("\n{0}", proxy_pwd);

            File.WriteAllText(Path.Combine(CURRENT_DIR, "proxy.txt"), proxy_file);

            ProxyVerifyBox.Visible = false;
            SetAllEnabled(true);
            WriteStatus("To delete all traces of proxy settings, delete the proxy.txt file!");
        }

        private void clearButton_MouseEnter(object sender, EventArgs e)
        {
            // expand clear button
            /*button3.Left = 194;
            button3.Size = new System.Drawing.Size(68, 21);*/
            clearButton.Text = "Clear";
            //button3.ImageAlign = ContentAlignment.MiddleLeft;
        }

        private void clearButton_MouseLeave(object sender, EventArgs e)
        {
            // shrink clear button
            /*button3.Left = 239;
            button3.Size = new System.Drawing.Size(23, 21);*/
            if (Type.GetType ("Mono.Runtime") == null)
                clearButton.Text = String.Empty;
            //button3.ImageAlign = ContentAlignment.MiddleCenter;
        }

        private void saveaswadbtn_MouseEnter(object sender, EventArgs e)
        {
            /*saveaswadbtn.Left = 190;
            saveaswadbtn.Size = new Size(72, 22);*/
            saveaswadbtn.Text = "Save As";
            /*saveaswadbtn.ImageAlign = ContentAlignment.MiddleLeft;*/
        }

        private void saveaswadbtn_MouseLeave(object sender, EventArgs e)
        {
            /*saveaswadbtn.Left = 230;
            saveaswadbtn.Size = new Size(32, 22);*/
            if (Type.GetType("Mono.Runtime") == null)
                saveaswadbtn.Text = String.Empty;
            //saveaswadbtn.ImageAlign = ContentAlignment.MiddleCenter;
        }

        void nus_script_item_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem tsmi = (ToolStripMenuItem)sender;
            string folderpath = "";
            if (!tsmi.OwnerItem.Equals(this.scriptsLocalMenuEntry))
            {
                folderpath = Path.Combine(tsmi.OwnerItem.Text, folderpath);
            }
            folderpath = Path.Combine(this.CURRENT_DIR, Path.Combine("scripts", Path.Combine(folderpath, tsmi.Text)));
            script_filename = folderpath;
            BackgroundWorker scripter = new BackgroundWorker();
            scripter.DoWork += new DoWorkEventHandler(RunScript);
            scripter.RunWorkerAsync();
        }

        private void saveaswadbtn_Click(object sender, EventArgs e)
        {
            SaveFileDialog wad_saveas = new SaveFileDialog();
            wad_saveas.Title = "Save WAD File...";
            wad_saveas.Filter = "WAD Files|*.wad|All Files|*.*";
            wad_saveas.AddExtension = true;
            DialogResult dres = wad_saveas.ShowDialog();
            if (dres != DialogResult.Cancel)
                WAD_Saveas_Filename = wad_saveas.FileName;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // This prevents errors when exiting before the database is parsed.
            // This is also probably not the best way to accomplish this...
            Environment.Exit(0);
        }

        private void iosPatchCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (iosPatchCheckbox.Checked == true)
            {
                //packbox.Enabled = false;
                packbox.Checked = true;
                SetAllEnabled(false);
                iosPatchGroupBox.Visible = true;
                iosPatchGroupBox.Enabled = true;
                iosPatchesListBox.Enabled = true;
                iosPatchGroupBoxOKbtn.Enabled = true;
            }
            else
            {
                //packbox.Enabled = true;
            }
        }

        private void iosPatchGroupBoxOKbtn_Click(object sender, EventArgs e)
        {
            SetAllEnabled(true);
            iosPatchGroupBox.Visible = false;
            if (iosPatchesListBox.CheckedIndices.Count == 0)
                // Uncheck the checkbox to indicate no patches
                iosPatchCheckbox.Checked = false;
            //packbox.Enabled = false;
        }

        private void FillDatabaseScripts()
        {
            Database databaseObj = new Database();
            databaseObj.LoadDatabaseToStream(Path.Combine(CURRENT_DIR, "database.xml"));

            ToolStripMenuItem[] scriptItems = databaseObj.LoadScripts();
            for (int a = 0; a < scriptItems.Length; a++)
            {
                scriptItems[a].DropDownItemClicked += new ToolStripItemClickedEventHandler(ScriptItem_Clicked);
                
                AddToolStripItemToStrip(scriptsDatabaseToolStripMenuItem, scriptItems[a]);
                //SystemMenuList.DropDownItems.Add(systemItems[a]);
            }
            SetPropertyThreadSafe(scriptsDatabaseToolStripMenuItem, true, "Enabled");
            SetPropertyThreadSafe(scriptsDatabaseToolStripMenuItem, true, "Visible");
        }

        public void ScriptItem_Clicked(object sender, ToolStripItemClickedEventArgs e)
        { }
    }
}