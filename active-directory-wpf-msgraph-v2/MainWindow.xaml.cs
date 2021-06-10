using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace active_directory_wpf_msgraph_v2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
 
    public partial class MainWindow : Window
    {
        //Set the API Endpoint to Graph 'me' endpoint. 
        // To change from Microsoft public cloud to a national cloud, use another value of graphAPIEndpoint.
        // Reference with Graph endpoints here: https://docs.microsoft.com/graph/deployments#microsoft-graph-and-graph-explorer-service-root-endpoints
        //string graphAPIEndpoint = "https://graph.microsoft.com/v1.0/me/calendar/events?$select=subject,bodyPreview,start,end";
        //string graphAPIEndpoint = "https://graph.microsoft.com/v1.0/groups/2e14d454-96b3-45ca-ad39-407c1599bad5/calendar/events";
        string graphAPIEndpoint = "https://graph.microsoft.com/v1.0/me/calendar/calendarView?startDateTime=2021-05-01T19:00:00&endDateTime=2021-05-20T19:00:00&$select=subject,bodyPreview,start,end";
        //Set the scope for API call to user.read
        string[] scopes = new string[] { "calendars.read", "user.read" };


        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Call AcquireToken - to acquire a token requiring user to sign-in
        /// </summary>
        private async void CallGraphButton_Click(object sender, RoutedEventArgs e)
        {
            AuthenticationResult authResult = null;
            var app = App.PublicClientApp;
            ResultText.Text = string.Empty;
            TokenInfoText.Text = string.Empty;

            IAccount firstAccount;

            switch(howToSignIn.SelectedIndex)
            {
                // 0: Use account used to signed-in in Windows (WAM)
                case 0:
                    // WAM will always get an account in the cache. So if we want
                    // to have a chance to select the accounts interactively, we need to
                    // force the non-account
                    firstAccount = PublicClientApplication.OperatingSystemAccount;
                    break;

                //  1: Use one of the Accounts known by Windows(WAM)
                case 1:
                    // We force WAM to display the dialog with the accounts
                    firstAccount = null;
                    break;

                //  Use any account(Azure AD). It's not using WAM
                default:
                    var accounts = await app.GetAccountsAsync();
                    firstAccount = accounts.FirstOrDefault();
                    break;
            }

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
                    ResultText.Text = $"Error Acquiring Token:{System.Environment.NewLine}{msalex}";
                }
            }
            catch (Exception ex)
            {
                ResultText.Text = $"Error Acquiring Token Silently:{System.Environment.NewLine}{ex}";
                return;
            }

            if (authResult != null)
            {
                ResultText.Text = await GetHttpContentWithToken(graphAPIEndpoint, authResult.AccessToken);
                DisplayBasicTokenInfo(authResult);
                this.SignOutButton.Visibility = Visibility.Visible;
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

                TimeSlotListCreate(eventArr);
                return content;
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        /// <summary>
        /// Sign out the current user
        /// </summary>
        private async void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            var accounts = await App.PublicClientApp.GetAccountsAsync();
            if (accounts.Any())
            {
                try
                {
                    await App.PublicClientApp.RemoveAsync(accounts.FirstOrDefault());
                    this.ResultText.Text = "User has signed-out";
                    this.CallGraphButton.Visibility = Visibility.Visible;
                    this.SignOutButton.Visibility = Visibility.Collapsed;
                }
                catch (MsalException ex)
                {
                    ResultText.Text = $"Error signing-out user: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// Display basic information contained in the token
        /// </summary>
        private void DisplayBasicTokenInfo(AuthenticationResult authResult)
        {
            TokenInfoText.Text = "";
            if (authResult != null)
            {
                TokenInfoText.Text += $"Username: {authResult.Account.Username}" + Environment.NewLine;
                TokenInfoText.Text += $"Token Expires: {authResult.ExpiresOn.ToLocalTime()}" + Environment.NewLine;
            }
        }

        private void UseWam_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SignOutButton_Click(sender, e);
            App.CreateApplication(howToSignIn.SelectedIndex != 2); // Not Azure AD accounts (that is use WAM accounts)
        }

        private void TimeSlotListCreate(JArray array) {


            if (array.Count != 0)
            {
                dynamic dynaEvnArr = array as dynamic;

                List<timeSlot> ts = new List<timeSlot>();
                TimeSpan timeDiff = new TimeSpan();
                //Debug.WriteLine($"test1, event number = {calendarView.Count}");

                for (int i = 0; i < array.Count; i++)
                {
                    if (i == 0)
                    { //calculate for first event now
                        timeDiff = TimeSlotCal(dynaEvnArr[i].start.dateTime.ToString(), dynaEvnArr[i].end.dateTime.ToString());
                        if (timeDiff.TotalMinutes > 0)
                        {
                            ts.Add(item: new timeSlot { state = "free", timeLength = timeDiff.TotalMinutes });
                        }
                        else
                        {
                            ts.Add(item: new timeSlot { state = "busy", timeLength = Math.Abs(timeDiff.TotalMinutes) });
                        }
                    }
                    else
                    {
                        timeDiff = TimeSlotCal(dynaEvnArr[i].start.dateTime.ToString(), dynaEvnArr[i].end.dateTime.ToString());
                        ts.Add(item: new timeSlot { state = "busy", timeLength = timeDiff.TotalMinutes });
                        try
                        {
                            timeDiff = TimeSlotCal(dynaEvnArr[i].start.dateTime.ToString(), dynaEvnArr[i+1].end.dateTime.ToString());

                            ts.Add(item: new timeSlot { state = "free", timeLength = timeDiff.TotalMinutes });
                        }
                        catch
                        {
                            timeDiff = TimeSlotCal(dynaEvnArr[i].end.dateTime.ToString(), dynaEvnArr[i].start.dateTime.ToString());

                            ts.Add(item: new timeSlot { state = "free", timeLength = timeDiff.TotalMinutes });
                        }
                    }

                }

                ts.ForEach(vm =>
                {
                    Console.WriteLine($"ts0 state = {vm.state}, long = {vm.timeLength}");
                }
                );

                //Console.WriteLine("total time = {0}", diff.TotalMinutes);

            }

        }

        private TimeSpan TimeSlotCal(String start, String end)
        {
            //DateTime startTime = new DateTime();
            //DateTime startTime = new DateTime(2021, 05, 05, 12, 00, 00).ToUniversalTime();

            DateTime startTime = DateTime.ParseExact(start, "M/d/yyyy h:m:s tt", null);
            DateTime endTime = DateTime.ParseExact(end, "M/d/yyyy h:m:s tt", null);

            TimeSpan diff = endTime - startTime;
            
            Console.WriteLine($"event spend time = {diff.TotalMinutes}");

            return diff;
        }

        public class timeSlot
        {
            public string state { get; set; }

            public double timeLength { get; set; }
        }
    }
}
