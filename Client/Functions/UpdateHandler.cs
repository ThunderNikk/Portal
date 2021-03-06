﻿using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Client.Network;
using DataCore;

namespace Client.Functions
{
    public class UpdateHandler
    {
        public static UpdateHandler Instance { get { if (instance == null) { instance = new UpdateHandler(); } return instance; } }

        public List<Structures.IndexEntry> FileList { get; set; }

        #region Constructors

        public UpdateHandler()
        {
            Core = new Core();
            Core.TotalMaxDetermined += (o, x) => { GUI.Instance.Invoke(new MethodInvoker(delegate { GUI.Instance.UpdateProgressMaximum(0, x.Maximum); })); };
            Core.TotalProgressChanged += (o, x) =>
            {
                GUI.Instance.Invoke(new MethodInvoker(delegate
                {
                    GUI.Instance.UpdateProgressValue(0, x.Value);
                    GUI.Instance.totalStatus.Text = x.Status;
                }));
            };
            Core.TotalProgressReset += (o, x) =>
            {
                GUI.Instance.Invoke(new MethodInvoker(delegate
                {
                    GUI.Instance.UpdateProgressValue(0, 0);
                    GUI.Instance.UpdateProgressMaximum(0, 100);
                    GUI.Instance.totalStatus.ResetText();
                }));
            };
            Core.CurrentMaxDetermined += (o, x) => { GUI.Instance.UpdateProgressMaximum(1, (int)x.Maximum); };
            Core.CurrentProgressChanged += (o, x) =>
            {
                GUI.Instance.Invoke(new MethodInvoker(delegate
                {
                    GUI.Instance.UpdateProgressValue(1, (int)x.Value);
                    GUI.Instance.UpdateStatus(1, x.Status);
                }));
            };
            Core.CurrentProgressReset += (o, x) =>
            {
                GUI.Instance.Invoke(new MethodInvoker(delegate
                {
                    GUI.Instance.UpdateProgressValue(1, 0);
                    GUI.Instance.UpdateProgressMaximum(1, 100);
                    GUI.Instance.currentStatus.ResetText();
                }));
            };
            Core.WarningOccured += (o, x) => { MessageBox.Show(x.Warning, "DataCore Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning); };
            Core.ErrorOccured += (o, x) => { MessageBox.Show(x.Error, "DataCore Exception", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            resourceFolder = string.Concat(OPT.Instance.GetString("clientdirectory"), @"/Resource/");
            disabledFolder = string.Concat(OPT.Instance.GetString("clientdirectory"), @"/Disabled/");
            tmpDirectory = string.Concat(Directory.GetCurrentDirectory(), @"/tmp/");
            FileList = new List<Structures.IndexEntry>();
            gDrive = new Drive();
        }

        #endregion

        #region Fields (private)

        private OPT settings = OPT.Instance;
        private static UpdateHandler instance;
        private List<DataCore.Structures.IndexEntry> index;
        private int receiveType = 0;
        private string indexPath;
        private string resourceFolder;
        private string disabledFolder;
        private string tmpDirectory;
        private int currentIndex;
        private bool updateData = false;
        private bool updatingData = false;
        private bool updateResource = false;
        private bool updatingResource = false;
        private Drive gDrive;
        private bool downloading = false;

        #endregion

        #region Fields (Public)

        public readonly Core Core;

        #endregion

        #region Methods (private)

        // TODO: This method can be improved
        private string getExtension(string fileName) { return (Core.IsEncoded(fileName)) ? Path.GetExtension(Core.DecodeName(fileName)).Remove(0, 1).ToLower() : Path.GetExtension(fileName).Remove(0, 1).ToLower(); }

        private void executeUpdate()
        {
            if (updateData && !updateResource || updateData && updateResource)
            {
                updatingData = true;
                ServerPackets.Instance.CS_RequestDataUpdateIndex();
            }
            else if (!updateData && updateResource)
            {
                updatingResource = true;
                ServerPackets.Instance.CS_RequestResourceUpdateIndex();
            }
            else if (!updateData && !updateResource) { GUI.Instance.OnUpdateComplete(); }
        }

        private void compareDataEntries()
        {
            GUI.Instance.UpdateStatus(0, "Comparing data entries...");
            GUI.Instance.UpdateProgressMaximum(0, FileList.Count);
            GUI.Instance.UpdateProgressValue(0, currentIndex);

            if (FileList.Count > 0)
            {
                Structures.IndexEntry file = FileList[currentIndex];

                DataCore.Structures.IndexEntry fileEntry = Core.GetEntry(ref index, file.FileName);

                if (fileEntry != null)
                {
                    GUI.Instance.UpdateStatus(1, string.Format("Checking {0}...", file.FileName));

                    string fileHash = Core.GetFileSHA512(settings.GetString("clientdirectory"), Core.GetID(fileEntry.Name), fileEntry.Offset, fileEntry.Length, getExtension(fileEntry.Name));

                    if (file.FileHash != fileHash)
                    {
                        GUI.Instance.UpdateStatus(1, string.Format("Downloading {0}...", FileList[currentIndex].FileName));
                        downloadUpdate();
                    }
                }
            }
            else { iterateCurrentIndex(); }
        }

        private void compareResourceEntries()
        {
            GUI.Instance.UpdateStatus(0, "Comparing resource files...");
            GUI.Instance.UpdateProgressMaximum(0, FileList.Count);
            GUI.Instance.UpdateProgressValue(0, currentIndex);

            if (FileList.Count > 0)
            {
                Structures.IndexEntry file = FileList[currentIndex];

                if (file != null)
                {
                    string resourceName = string.Concat(resourceFolder, file.FileName);

                    if (!File.Exists(resourceName) || (Hash.GetSHA512Hash(resourceName) != file.FileHash))
                    {
                        GUI.Instance.UpdateStatus(1, string.Format("Downloading {0}...", FileList[currentIndex].FileName));
                        downloadUpdate();
                    }
                    else { iterateCurrentIndex(); }
                }
            }
            else
            {
                GUI.Instance.UpdateStatus(0, "Updating resource folder time...");
                Directory.SetLastWriteTimeUtc(resourceFolder, DateTime.UtcNow);

                iterateCurrentIndex();
            }
        }

        private void downloadUpdate()
        {
            switch (receiveType)
            {
                case 0: // Google Drive
                    // TODO: Adjust me for new downloading system
                    //gDrive.Start();
                    //string filePath = gDrive.GetFile(name);
                    break;

                case 1: // HTTP
                    break;

                case 2: // FTP
                    break;

                case 3: // TCP
                    ServerPackets.Instance.CS_RequestFileSize(FileList[currentIndex].FileName);
                    break;

                case 99:
                    MessageBox.Show("Invalid receiveType!", "UpdateHandler Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
            }
        }

        private void iterateCurrentIndex()
        {
            this.currentIndex++;

            if (this.currentIndex == this.FileList.Count && updatingData && !updateResource || this.FileList.Count == 0 && !updateResource) { GUI.Instance.OnUpdateComplete(); }
            else if (this.currentIndex == this.FileList.Count && updatingData && updateResource || this.FileList.Count == 0 && updatingData && updateResource)
            {
                updateData = updatingData = false;
                executeUpdate();
            }
            else if (this.currentIndex < this.FileList.Count && updatingData) { compareDataEntries(); }

            if (this.currentIndex == this.FileList.Count && updatingResource || this.FileList.Count == 0 && updatingResource) { GUI.Instance.OnUpdateComplete(); }
            else if (this.currentIndex < this.FileList.Count && updatingResource) { compareResourceEntries(); }
        }

        #endregion

        #region Methods (Public)

        public void Start()
        {
            indexPath = Path.Combine(OPT.Instance.GetString("clientdirectory"), "data.000");
            GUI.Instance.UpdateStatus(1, "Loading data index...");
            index = Core.Load(indexPath, false, 64000);

            if (!Directory.Exists(disabledFolder)) { Directory.CreateDirectory(disabledFolder); }
            if (!Directory.Exists(tmpDirectory)) { Directory.CreateDirectory(tmpDirectory); }

            ServerPackets.Instance.CS_RequestTransferType();
        }

        public void OnUpdateDateTimeReceived(DateTime dateTime)
        {
            GUI.Instance.UpdateStatus(0, "Checking times...");

            DateTime indexDateTime = File.GetLastWriteTimeUtc(indexPath);
            DateTime resourceDateTime = Directory.GetLastWriteTimeUtc(resourceFolder);

            updateData = indexDateTime < dateTime;
            updateResource = resourceDateTime < dateTime;

            executeUpdate();
        }

        public void OnDataEntryReceived(string fileName, string hash)
        {
            this.FileList.Add(new Structures.IndexEntry() { FileName = fileName, FileHash = hash });
        }

        public void OnResourceEntryReceived(string fileName, string fileHash, bool delete) { this.FileList.Add(new Structures.IndexEntry() { FileName = fileName, FileHash = fileHash, Delete = delete }); }

        public void OnDataIndexEOF()
        {
            this.currentIndex = 0;
            compareDataEntries();
        }

        public void OnResourceIndexEOF()
        {
            this.currentIndex = 0;
            compareResourceEntries();
        }

        public void OnSendTypeReceived(int transferType)
        {
            receiveType = transferType;

            ServerPackets.Instance.CS_GetUpdateDateTime();
        }

        public void OnFileInfoReceived(string archiveName) { ServerPackets.Instance.CS_RequestFileTransfer(archiveName); }

        public void OnFileTransfered(string zipName)
        {
            GUI.Instance.ResetProgressStatus(1);

            GUI.Instance.UpdateStatus(0, "Applying update...");

            string fileName = FileList[currentIndex].FileName;

            bool isLegacy = updatingResource;

            bool isNew = !Core.EntryExists(ref index, fileName);

            string zipPath = string.Format(@"{0}\Downloads\{1}", Directory.GetCurrentDirectory(), zipName);


            if (isLegacy)
            {
                GUI.Instance.UpdateStatus(1, string.Format("Moving {0} to /Resource/...", fileName));

                // TODO: Rename? Move? Delete if already exists

                // Extract the zip to the /resource/ folder of client
                ZIP.Unpack(zipPath, resourceFolder);
            }
            else
            {
                string filePath = string.Format(@"{0}\{1}", tmpDirectory, fileName);

                // Extract the zip to the /tmp/ folder for processing
                ZIP.Unpack(zipPath, tmpDirectory);

                DataCore.Structures.IndexEntry indexEntry = Core.GetEntry(ref index, fileName);

                if (indexEntry != null)
                {
                    if (!isNew)
                    {
                        GUI.Instance.UpdateStatus(1, string.Format("Updating indexed file: {0}...", fileName));
                        Core.UpdateFileEntry(ref index, settings.GetString("clientdirectory"), filePath, 0);
                    }
                    else
                    {
                        GUI.Instance.UpdateStatus(1, string.Format("Inserting file: {0}...", fileName));
                        Core.ImportFileEntry(ref index, settings.GetString("clientdirectory"), filePath, 0);
                    }

                    GUI.Instance.UpdateStatus(1, "Finalizing data index...");
                    Core.Save(ref index, settings.GetString("clientdirectory"), false);
                }
            }

            GUI.Instance.UpdateStatus(1, "Cleaning up...");

            // Delete the zip
            File.Delete(zipPath);

            // Clear the tmp folder
            foreach (string tmpFileName in Directory.GetFiles(tmpDirectory)) { File.Delete(tmpFileName); }

            // Increase the currentIndex
            iterateCurrentIndex();
        }

        public void OnWaitReceived(ushort packetID, int period)
        {
            System.Threading.Thread.Sleep(period);
            ServerManager.Instance.Send(new PacketStream(packetID));
        }

        public void ExecuteSelfUpdate()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = @"Updater.exe";
            Process.Start(startInfo);
        }


        #endregion
    }
}
