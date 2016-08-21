﻿using System;
using System.Data;
using System.Data.SqlClient;
using Server.Network;

namespace Server.Functions
{
    // TODO: Implement database user validation
    public class UserHandler
    {
        protected bool debug = false;

        protected static UserHandler instance;
        public static UserHandler Instance
        {
            get
            {
                if (instance == null) { instance = new UserHandler(); }

                return instance;
            }
        }

        public UserHandler() { debug = OPT.GetBool("debug"); }

        public void OnUserRequestDesKey(Client client)
        {
            if (OPT.SettingExists("des.key"))
            {
                string desKey = OPT.GetString("des.key");

                PacketStream stream = new PacketStream(0x9999);
                stream.WriteString(desKey);

                if (OPT.GetBool("debug")) { Console.WriteLine("[{0}] Sent!", desKey); }

                ClientManager.Instance.Send(client, stream);
            }
            else { /*TODO: Report Error*/ }
        }

        public void OnValidateUser(Client client, string username, string password, string fingerprint)
        {
            if (debug)
            {
                Console.WriteLine("Client [{0}] requested login validation with the following credentials:", client.Id);
                Console.WriteLine("Username: {0}\nPassword: {1}\nFingerprint: {2}", username, password, fingerprint);
            }

            // Check if username / password exist
            using (SqlConnection sqlCon = Database.Connection)
            {
                SqlCommand sqlCmd = new SqlCommand();

                sqlCmd.Connection = sqlCon;
                sqlCmd.CommandText = string.Format("SELECT account_id FROM dbo.{0} WHERE login_name = @name AND password = @password", OPT.GetString("db.auth.table.alias"));
                sqlCmd.Parameters.Add("@name", SqlDbType.NVarChar).Value = username;
                sqlCmd.Parameters.Add("@password", SqlDbType.NVarChar).Value = PasswordCipher.CreateHash(OPT.GetString("md5.key"), password);

                if (debug) { Console.Write("\t-Checking for Account..."); }

                object result = Database.ExecuteStatement(sqlCmd, 1);

                if (debug) { Console.WriteLine(((int)result > 0) ? "[FOUND]" : "[NOT FOUND]"); }

                if ((int)result > 0) // Account exists
                {
                    int account_id = (int)result;

                    if (debug) { Console.Write("Checking Account ban status..."); }

                    // Check if account is banned
                    sqlCmd.CommandText = string.Format("SELECT ban FROM dbo.{0} WHERE login_name = @name AND password = @password", OPT.GetString("db.auth.table.alias"));
                    result = Database.ExecuteStatement(sqlCmd, 1);

                    if (debug) { Console.WriteLine(((int)result == 0) ? "[NOT BANNED]" : "[BANNED]"); }

                    if ((int)result == 0) // Account is not banned
                    {
                        if (debug) { Console.Write("\t-Checking for FingerPrint..."); }

                        // Check for fingerprint
                        sqlCmd.CommandText = "SELECT COUNT(account_id) FROM dbo.FingerPrint WHERE account_id = @account_id";
                        sqlCmd.Parameters.Clear();
                        sqlCmd.Parameters.Add("@account_id", SqlDbType.Int).Value = account_id;

                        result = Database.ExecuteStatement(sqlCmd, 1);

                        if (debug) { Console.WriteLine(((int)result == 1) ? "[FOUND]" : "[NOT FOUND]"); }

                        if ((int)result == 1) // FingerPrint exists
                        {
                            if (debug) { Console.Write("\t-Checking FingerPrint ban status..."); }

                            // Check if FingerPrint is banned
                            sqlCmd.CommandText = "SELECT ban FROM dbo.FingerPrint WHERE account_id = @account_id";

                            result = Database.ExecuteStatement(sqlCmd, 1);

                            if (debug) { Console.WriteLine(((int)result == 0) ? "[NOT BANNED]" : "[BANNED]"); }

                            if ((int)result == 0) // FingerPrint is not banned
                            {
                                setOTP(ref client, ref sqlCmd, account_id);
                            }
                            else // FingerPrint is banned
                            {
                                if (debug) { Console.Write("\t-Checking if FingerPrint ban is expired..."); }

                                // Get OTP Expiration Date
                                sqlCmd.CommandText = "SELECT expiration_date FROM dbo.FingerPrint WHERE account_id = @account_id";
                                sqlCmd.Parameters.Clear();
                                sqlCmd.Parameters.Add("@account_id", SqlDbType.Int).Value = account_id;

                                result = Database.ExecuteStatement(sqlCmd, 1);

                                if ((DateTime)result < DateTime.Now) // Ban is up
                                {
                                    if (debug) { Console.WriteLine("[EXPIRED]\n\t-Updating FingerPrint ban..."); }

                                    sqlCmd.CommandText = "UPDATE dbo.FingerPrint SET ban = 0 WHERE account_id = @account_id";

                                    result = Database.ExecuteStatement(sqlCmd, 0);

                                    if (debug) { Console.WriteLine(((int)result == 1) ? "[SUCCESS]" : "[FAIL]"); }

                                    setOTP(ref client, ref sqlCmd, account_id);
                                }
                                else { ClientPackets.Instance.SendBanStatus(client, 1); }
                            }
                        }
                        else
                        {
                            if (debug) { Console.Write("\t-Inserting FingerPrint: {0}...", fingerprint); }

                            sqlCmd.CommandText = "INSERT INTO dbo.FingerPrint (account_id, finger_print, ban, expiration_date) VALUES (@account_id, @finger_print, @ban, @expiration_date)";
                            sqlCmd.Parameters.Clear();
                            sqlCmd.Parameters.Add("@account_id", SqlDbType.Int).Value = account_id;
                            sqlCmd.Parameters.Add("@finger_print", SqlDbType.NVarChar).Value = fingerprint;
                            sqlCmd.Parameters.Add("@ban", SqlDbType.Int).Value = 0;
                            sqlCmd.Parameters.Add("@expiration_date", SqlDbType.DateTime).Value = new DateTime(1999, 1, 1, 12, 0, 0, 0);

                            result = Database.ExecuteStatement(sqlCmd, 0);

                            if (debug) { Console.WriteLine(((int)result == 1) ? "[SUCCESS]" : "[FAIL]"); }

                        }
                    }
                    else { ClientPackets.Instance.SendBanStatus(client, 0); } // Account is banned
                }
                else { ClientPackets.Instance.SendAccountNull(client); } // Account doesn't exist
            }
        }

        protected void setOTP(ref Client client, ref SqlCommand sqlCmd, int account_id)
        {
            // Formulate an OTP
            string otpHash = OTP.GenerateRandomPassword(26);

            if (debug) { Console.WriteLine("\t-Generated OTP: {0}", otpHash); }

            // Check if OTP account_id already exists
            sqlCmd.CommandText = "SELECT COUNT(account_id) FROM dbo.OTP WHERE account_id = @account_id";

            object result = Database.ExecuteStatement(sqlCmd, 1);

            if ((int)result == 1) // OTP account_id exists, update OTP
            {
                if (debug) { Console.Write("\t-Updating OTP..."); }

                sqlCmd.CommandText = "UPDATE dbo.OTP SET otp = @OTP WHERE account_id = @account_id";
                sqlCmd.Parameters.Add("@OTP", SqlDbType.NVarChar).Value = otpHash;

                result = Database.ExecuteStatement(sqlCmd, 0);

                if (debug) { Console.WriteLine(((int)result == 1) ? "[SUCCESS]" : "[FAIL]"); }
            }
            else // OTP account_id doesn't exist, write new OTP
            {
                if (debug) { Console.Write("\t-Inserting OTP..."); }

                sqlCmd.CommandText = "INSERT INTO dbo.OTP (account_id, otp) VALUES (@account_id, @OTP)";
                sqlCmd.Parameters.Clear();
                sqlCmd.Parameters.Add("@account_id", SqlDbType.Int).Value = account_id;
                sqlCmd.Parameters.Add("@OTP", SqlDbType.NVarChar).Value = otpHash;

                result = Database.ExecuteStatement(sqlCmd, 0);

                if (debug) { Console.WriteLine(((int)result == 1) ? "[SUCCESS]" : "[FAIL]"); }
            }

            ClientPackets.Instance.OTP(client, otpHash);
        }
    }
}