using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

using Microsoft.Identity.Client;
using System.Windows.Interop;
using Newtonsoft.Json.Linq;
using System.Management;

using smart_charge_battery_info;
using CsvHelper;
using System.IO;
using System.Globalization;
using System.Windows.Threading;
using CsvHelper.Configuration.Attributes;

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
            App.CreateApplication(false); // use Azure AD account

            InitializeComponent();

            //log to CSV every 10 mins
            DispatcherTimer _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMinutes(10);
            _timer.Tick += PConsumption_to_CSV;
            _timer.Start();
        }

        private static void PConsumption_to_CSV(object sender, EventArgs e)
        {
            BatteryInformation cap = BatteryInfo.GetBatteryInformation();

            if (Math.Sign(cap.DischargeRate) != -1)
            {
                return; //does not need to record data if not in discharge;
            }

            //log discharge rate to csv file
            Power_Consumption_Data pcw = new Power_Consumption_Data();
            pcw.Index = 1;
            pcw.Discharge_Rate = Math.Abs(cap.DischargeRate);
//            pcw.dt = DateTime.Now;

            using (var writer = new StreamWriter("pcw.csv", true))
            {
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.WriteRecord(pcw);
                    csv.NextRecord();
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            MainWindow main = new MainWindow();
            main.Show();
            this.Close();
        }

        private async void Calculate_Button_Click(object sender, RoutedEventArgs e)
        {

            if (Calendar_Radio.IsChecked == true)
            {
                Process_With_Calendar();
            }
            else if (User_Select_Radio.IsChecked == true)
            {
                Process_With_Select();
            }
        }

        private async void Process_With_Calendar()
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
            }

        }

        private void Process_With_Select()
        {
            DebugText.Clear();
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

                // create a time slot list with busy/free state and length
                List<timeSlot> ts = TimeSlotListCreate(eventArr);
                
                BatteryStatusGet();
                BatteryInformation cap = BatteryInfo.GetBatteryInformation();
                Console.WriteLine("battery full capacity = {0}, current capacity = {1}, charge rate = {2}", cap.FullChargeCapacity, cap.CurrentCapacity, cap.DischargeRate);

                int chargeSpeed = ChargeSpeedCal(cap, ts);
                //WmiExecute();
                
                return content;
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        private List<timeSlot> TimeSlotListCreate(JArray array)
        {

            List<timeSlot> ts = new List<timeSlot>();
            if (array.Count != 0)
            {
                dynamic dynaEvnArr = array as dynamic;

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
                            timeDiff = TimeSlotCal(dynaEvnArr[i].start.dateTime.ToString(), dynaEvnArr[i + 1].end.dateTime.ToString());

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
            return ts;
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

        private void BatteryStatusGet()
        {
            ManagementClass wmi = new ManagementClass("Win32_Battery");
            ManagementObjectCollection allBatteries = wmi.GetInstances();

            double batteryLevel = 0;

            foreach (var battery in allBatteries)
            {
                batteryLevel = Convert.ToDouble(battery["EstimatedChargeRemaining"]);

                Console.WriteLine("battery level  = {0}", batteryLevel);
            }
        }

        public class timeSlot
        {
            public string state { get; set; }

            public double timeLength { get; set; }
        }

        private Int32 ChargeSpeedCal(BatteryInformation batInfo, List<timeSlot> timeSlots)
        {
            Int32 fullChargeTime;
            Int32 dischargeRate=0, chargeRate, emptyTime, buffer = 1;
            bool IsCharge = false;
            int chargeCurrent = 0;

            using (var reader = new StreamReader("pcw.csv"))
            {
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    var records = new List<Power_Consumption_Data>();
                    double index = 0;
                    double rate_total = 0;
                    while (csv.Read())
                    {
                        index ++;
                        rate_total += csv.GetField<int>(1);
                        dischargeRate = (int)(rate_total / index);
                    }
                }
            }

            Console.WriteLine("discharge rate = " + dischargeRate);

            switch (Math.Sign(batInfo.DischargeRate))
            {
                case 1:
                    chargeRate = batInfo.DischargeRate;
                    //dischargeRate = 16796;
                    IsCharge = true;
                    break;
                case -1:
                    chargeRate = 48047;
                    //dischargeRate = Math.Abs(batInfo.DischargeRate);
                    IsCharge = false;
                    break;

                default:
                    chargeRate = 48047;
                    dischargeRate = 16976;
                    break;
            }

            if (!IsCharge) //return if not in charge state
                return chargeCurrent;

            fullChargeTime = ((batInfo.FullChargeCapacity - (int)batInfo.CurrentCapacity) * 10000
                / chargeRate) * 60 / 10000;

            emptyTime = ((int)batInfo.CurrentCapacity * buffer * 10000) /
                dischargeRate * 60 / 10000;

            Console.WriteLine("full charge time = {0}, empty time = {1}, voltage = {2}", fullChargeTime, emptyTime, batInfo.Voltage);

            double ACtime = 0;
            if (timeSlots[0].state.Equals("free"))
            {
                ACtime = timeSlots[0].timeLength;
                
                var c = (timeSlots[1].timeLength / 60) * dischargeRate; //needed Wh
                if (c > batInfo.CurrentCapacity) //full speed charge
                {
                    chargeCurrent = 0xFF * 100;
                }else
                {
                    //current(mA) = remain capacity(mWh) / charge time (in hours) / battery voltage (mV) * 1000
                    chargeCurrent = (int)((batInfo.FullChargeCapacity - (int)batInfo.CurrentCapacity) / (ACtime / 60) / 12200 * 1000);
                }
            }else
            {
                //first time slot is busy
                ACtime = timeSlots[0].timeLength + timeSlots[1].timeLength;
                
                var c = (timeSlots[2].timeLength / 60) * dischargeRate; //needed Wh
                if (c > batInfo.CurrentCapacity) //full speed charge
                {
                    chargeCurrent = 0xFF * 100;
                }
                else
                {
                    //current(mA) = remain capacity(mWh) / charge time (in hours) / battery voltage (mV) * 1000
                    chargeCurrent = (int)((batInfo.FullChargeCapacity - (int)batInfo.CurrentCapacity) / (ACtime / 60) / 12200 * 1000);
                }
            }

            MessageBox.Show("you will be charge in "+ chargeCurrent+"mA!");
            return chargeCurrent / 100;
        }

        public void WmiExecute()
        {
            ManagementObject classInstance = new ManagementObject("root\\WMI", "LCFC_SetChargeSpeed.InstanceName='ACPI\\PNP0C14\\1_0'", null);

            // Obtain in-parameters for the method
            ManagementBaseObject inParams =
                classInstance.GetMethodParameters("SetChargeSpeed");

            // Add the input parameters.
            inParams["parameter"] = "2";

            // Execute the method and obtain the return values.
            ManagementBaseObject outParams =
                classInstance.InvokeMethod("SetChargeSpeed", inParams, null);

            // List outParams
            Console.WriteLine("Out parameters:");
            Console.WriteLine("return: " + outParams["return"]);
        }

    }

    internal class Power_Consumption_Data
    {
        [Index(0)]
        public int Index { get; set; }

        [Index(1)]
        public int Discharge_Rate { get; set; }

        [Index(2)]
        public DateTime dt { get; set; }
    }

}
