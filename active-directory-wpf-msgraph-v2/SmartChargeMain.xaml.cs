using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Microsoft.Identity.Client;
using System.Windows.Interop;
using Newtonsoft.Json.Linq;
using System.Management;
using Windows.System.Power;
using System.Runtime.InteropServices;

using System.IO;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;

namespace active_directory_wpf_msgraph_v2
{
    /// <summary>
    /// Interaction logic for SmartChargeMain.xaml
    /// </summary>
    public partial class SmartChargeMain : Window
    {

        //Set the API Endpoint to Graph 'me' endpoint. 
        // To change from Microsoft public cloud to a national cloud, use another value of graphAPIEndpoint.
        // Reference with Graph endpoints here: https://docs.microsoft.com/graph/deployments#microsoft-graph-and-graph-explorer-service-root-endpoints
        //string graphAPIEndpoint = "https://graph.microsoft.com/v1.0/me/calendar/events?$select=subject,bodyPreview,start,end";
        //string graphAPIEndpoint = "https://graph.microsoft.com/v1.0/groups/2e14d454-96b3-45ca-ad39-407c1599bad5/calendar/events";
        string graphAPIEndpoint = "https://graph.microsoft.com/v1.0/me/calendar/calendarView?startDateTime=2021-05-01T19:00:00&endDateTime=2021-05-20T19:00:00&$select=subject,bodyPreview,start,end";
        //Set the scope for API call to user.read
        string[] scopes = new string[] { "calendars.read", "user.read" };

        public SmartChargeMain()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            MainWindow main = new MainWindow();
            main.Show();
            this.Close();
        }

        private async void Calculate_Button_Click(object sender, RoutedEventArgs e)
        {
            AuthenticationResult authResult = null;
            var app = App.PublicClientApp;
            DebugText.Text = string.Empty;

            IAccount firstAccount;

            //  Use any account(Azure AD). It's not using WAM
            var accounts = await app.GetAccountsAsync();
            firstAccount = accounts.FirstOrDefault();
                  

            try
            {
                authResult = await app.AcquireTokenSilent(scopes, firstAccount)
                    .ExecuteAsync();
            }
            catch (MsalUiRequiredException ex)
            {
                // A MsalUiRequiredException happened on AcquireTokenSilent. 
                // This indicates you need to call AcquireTokenInteractive to acquire a token
                System.Diagnostics.Debug.WriteLine($"MsalUiRequiredException: {ex.Message}");

                try
                {
                    authResult = await app.AcquireTokenInteractive(scopes)
                        .WithAccount(firstAccount)
                        .WithParentActivityOrWindow(new WindowInteropHelper(this).Handle) // optional, used to center the browser on the window
                        .WithPrompt(Prompt.SelectAccount)
                        .ExecuteAsync();
                }
                catch (MsalException msalex)
                {
                    DebugText.Text = $"Error Acquiring Token:{System.Environment.NewLine}{msalex}";
                }
            }
            catch (Exception ex)
            {
                DebugText.Text = $"Error Acquiring Token Silently:{System.Environment.NewLine}{ex}";
                return;
            }

            if (authResult != null)
            {
                DebugText.Text = await GetHttpContentWithToken(graphAPIEndpoint, authResult.AccessToken);
                //DisplayBasicTokenInfo(authResult);
                //this.SignOutButton.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Perform an HTTP GET request to a URL using an HTTP Authorization header
        /// </summary>
        /// <param name="url">The URL</param>
        /// <param name="token">The token</param>
        /// <returns>String containing the results of the GET operation</returns>
        public async Task<string> GetHttpContentWithToken(string url, string token)
        {
            var httpClient = new System.Net.Http.HttpClient();
            System.Net.Http.HttpResponseMessage response;
            try
            {
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                //Add the token in Authorization header
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                response = await httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                JObject jo = JObject.Parse(content);
                JArray eventArr = (JArray)jo["value"];

                //List<timeSlot> ts = TimeSlotListCreate(eventArr);
                //BatteryStatusGet();
                //BatteryInformation cap = BatteryInfo.GetBatteryInformation();
                //Console.WriteLine("battery full capacity = {0}, current capacity = {1}, charge rate = {2}", cap.FullChargeCapacity, cap.CurrentCapacity, cap.DischargeRate);

                //int chargeSpeed = ChargeSpeedCal(cap, ts);
                //Console.WriteLine( RuntimeInformation.FrameworkDescription);
                //WmiExecute();
                return content;
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }
    }
}
