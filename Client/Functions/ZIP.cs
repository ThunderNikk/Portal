﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZLibNet;

namespace Client.Functions
{
    public class ZIP
    {
        /// <summary>
        /// Unzips file at provided in path to provided out path
        /// </summary>
        /// <param name="inPath">Path of the .zip to be unpacked</param>
        /// <param name="outDirectory">Directory to unpack .zip to</param>
        /// <returns></returns>
        public static bool Unpack(string inPath, string outDirectory)
        {
            try
            {
                string filePath = string.Format(@"{0}/{1}", outDirectory, Path.GetFileNameWithoutExtension(inPath));
                if (File.Exists(filePath)) { File.Delete(filePath); }

                UnZipper uz = new UnZipper();
                uz.IfFileExist = enIfFileExist.Overwrite;
                uz.Destination = outDirectory;
                uz.ItemList.Add("**");
                uz.ZipFile = inPath;
                uz.UnZip();

                return true;
            }
            catch (Exception ex) { System.Windows.Forms.MessageBox.Show(ex.Message, "Zlib Exception Occured", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error); }

            return false;
        }
    }
}
