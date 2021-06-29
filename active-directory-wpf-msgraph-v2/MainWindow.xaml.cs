using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Management;
using Windows.System.Power;
using System.Runtime.InteropServices;

using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

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

            switch (howToSignIn.SelectedIndex)
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

                List<timeSlot> ts = TimeSlotListCreate(eventArr);
                BatteryStatusGet();
                BatteryInformation cap = BatteryInfo.GetBatteryInformation();
                Console.WriteLine("battery full capacity = {0}, current capacity = {1}, charge rate = {2}", cap.FullChargeCapacity, cap.CurrentCapacity, cap.DischargeRate);

                int chargeSpeed = ChargeSpeedCal(cap, ts);
                //Console.WriteLine( RuntimeInformation.FrameworkDescription);
                WmiExecute();
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

        private List<timeSlot> TimeSlotListCreate(JArray array) {

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
            Int32 dischargeRate, chargeRate, emptyTime, buffer = 1;

            switch (Math.Sign(batInfo.DischargeRate))
            {
                case 1:
                    chargeRate = batInfo.DischargeRate;
                    dischargeRate = 16796;
                    break;
                case -1:
                    chargeRate = 48047;
                    dischargeRate = Math.Abs(batInfo.DischargeRate);
                    break;

                default:
                    chargeRate = 48047;
                    dischargeRate = 16976;
                    break;
            }

            fullChargeTime = ((batInfo.FullChargeCapacity - (int)batInfo.CurrentCapacity) * 10000
                / chargeRate) * 60 / 10000;

            emptyTime = ((int)batInfo.CurrentCapacity * buffer * 10000) /
                dischargeRate * 60 / 10000;

            Console.WriteLine("full charge time = {0}, empty time = {1}", fullChargeTime, emptyTime);

            double totalTime = 0;
            foreach (timeSlot t in timeSlots)
            {
                if (t.state.Equals("busy"))
                {
                    if (totalTime > fullChargeTime)
                    {
                        //reduce charge rate
                    }
                    break;
                }
                totalTime += t.timeLength;
            }


            // 1. compare with remaining
            // > --> keep current or slow down
            // < --> higher charge speed
            // 2. long free time

            return fullChargeTime;
        }

        public void WmiExecute() 
        {
            ManagementObject classInstance = new ManagementObject("root\\WMI","Lenovo_SetBiosSetting.InstanceName='ACPI\\PNP0C14\\1_0'",null);

            // Obtain in-parameters for the method
            ManagementBaseObject inParams =
                classInstance.GetMethodParameters("SetBiosSetting");

            // Add the input parameters.
            inParams["parameter"] = "WakeOnLAN,Enable;";

            // Execute the method and obtain the return values.
            ManagementBaseObject outParams =
                classInstance.InvokeMethod("SetBiosSetting", inParams, null);

            // List outParams
            Console.WriteLine("Out parameters:");
            Console.WriteLine("return: " + outParams["return"]);
        }
    
        public class BatteryInformation
        {
            public uint CurrentCapacity { get; set; }
            public int DesignedMaxCapacity { get; set; }
            public int FullChargeCapacity { get; set; }
            public uint Voltage { get; set; }
            public int DischargeRate { get; set; }
        }

        public static class BatteryInfo
        {

            public static BatteryInformation GetBatteryInformation()
            {
                IntPtr deviceDataPointer = IntPtr.Zero;
                IntPtr queryInfoPointer = IntPtr.Zero;
                IntPtr batteryInfoPointer = IntPtr.Zero;
                IntPtr batteryWaitStatusPointer = IntPtr.Zero;
                IntPtr batteryStatusPointer = IntPtr.Zero;
                try
                {
                    IntPtr deviceHandle = SetupDiGetClassDevs(
                    Win32.GUID_DEVCLASS_BATTERY, Win32.DEVICE_GET_CLASS_FLAGS.DIGCF_PRESENT | Win32.DEVICE_GET_CLASS_FLAGS.DIGCF_DEVICEINTERFACE);

                    Win32.SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new Win32.SP_DEVICE_INTERFACE_DATA();
                    deviceInterfaceData.CbSize = Marshal.SizeOf(deviceInterfaceData);

                    SetupDiEnumDeviceInterfaces(deviceHandle, Win32.GUID_DEVCLASS_BATTERY, 0, ref deviceInterfaceData);

                    deviceDataPointer = Marshal.AllocHGlobal(Win32.DEVICE_INTERFACE_BUFFER_SIZE);
                    //Win32.SP_DEVICE_INTERFACE_DETAIL_DATA deviceDetailData =
                    //    (Win32.SP_DEVICE_INTERFACE_DETAIL_DATA)Marshal.PtrToStructure(deviceDataPointer, typeof(Win32.SP_DEVICE_INTERFACE_DETAIL_DATA));

                    //toggle these two and see if naything changes... ^^^^^^^^^^^^
                    Win32.SP_DEVICE_INTERFACE_DETAIL_DATA deviceDetailData = new Win32.SP_DEVICE_INTERFACE_DETAIL_DATA();
                    deviceDetailData.CbSize = (IntPtr.Size == 8) ? 8 : 4 + Marshal.SystemDefaultCharSize;

                    SetupDiGetDeviceInterfaceDetail(deviceHandle, ref deviceInterfaceData, ref deviceDetailData, Win32.DEVICE_INTERFACE_BUFFER_SIZE);

                    IntPtr batteryHandle = CreateFile(deviceDetailData.DevicePath, FileAccess.ReadWrite, FileShare.ReadWrite, FileMode.Open, Win32.FILE_ATTRIBUTES.Normal);

                    Win32.BATTERY_QUERY_INFORMATION queryInformation = new Win32.BATTERY_QUERY_INFORMATION();

                    DeviceIoControl(batteryHandle, Win32.IOCTL_BATTERY_QUERY_TAG, ref queryInformation.BatteryTag);

                    Win32.BATTERY_INFORMATION batteryInformation = new Win32.BATTERY_INFORMATION();
                    queryInformation.InformationLevel = Win32.BATTERY_QUERY_INFORMATION_LEVEL.BatteryInformation;

                    int queryInfoSize = Marshal.SizeOf(queryInformation);
                    int batteryInfoSize = Marshal.SizeOf(batteryInformation);

                    queryInfoPointer = Marshal.AllocHGlobal(queryInfoSize);
                    Marshal.StructureToPtr(queryInformation, queryInfoPointer, false);

                    batteryInfoPointer = Marshal.AllocHGlobal(batteryInfoSize);
                    Marshal.StructureToPtr(batteryInformation, batteryInfoPointer, false);

                    DeviceIoControl(batteryHandle, Win32.IOCTL_BATTERY_QUERY_INFORMATION, queryInfoPointer, queryInfoSize, batteryInfoPointer, batteryInfoSize);

                    Win32.BATTERY_INFORMATION updatedBatteryInformation =
                        (Win32.BATTERY_INFORMATION)Marshal.PtrToStructure(batteryInfoPointer, typeof(Win32.BATTERY_INFORMATION));

                    Win32.BATTERY_WAIT_STATUS batteryWaitStatus = new Win32.BATTERY_WAIT_STATUS();
                    batteryWaitStatus.BatteryTag = queryInformation.BatteryTag;

                    Win32.BATTERY_STATUS batteryStatus = new Win32.BATTERY_STATUS();

                    int waitStatusSize = Marshal.SizeOf(batteryWaitStatus);
                    int batteryStatusSize = Marshal.SizeOf(batteryStatus);

                    batteryWaitStatusPointer = Marshal.AllocHGlobal(waitStatusSize);
                    Marshal.StructureToPtr(batteryWaitStatus, batteryWaitStatusPointer, false);

                    batteryStatusPointer = Marshal.AllocHGlobal(batteryStatusSize);
                    Marshal.StructureToPtr(batteryStatus, batteryStatusPointer, false);

                    DeviceIoControl(batteryHandle, Win32.IOCTL_BATTERY_QUERY_STATUS, batteryWaitStatusPointer, waitStatusSize, batteryStatusPointer, batteryStatusSize);

                    Win32.BATTERY_STATUS updatedStatus =
                        (Win32.BATTERY_STATUS)Marshal.PtrToStructure(batteryStatusPointer, typeof(Win32.BATTERY_STATUS));

                    Win32.SetupDiDestroyDeviceInfoList(deviceHandle);

                    return new BatteryInformation()
                    {
                        DesignedMaxCapacity = updatedBatteryInformation.DesignedCapacity,
                        FullChargeCapacity = updatedBatteryInformation.FullChargedCapacity,
                        CurrentCapacity = updatedStatus.Capacity,
                        Voltage = updatedStatus.Voltage,
                        DischargeRate = updatedStatus.Rate
                    };

                }
                finally
                {
                    Marshal.FreeHGlobal(deviceDataPointer);
                    Marshal.FreeHGlobal(queryInfoPointer);
                    Marshal.FreeHGlobal(batteryInfoPointer);
                    Marshal.FreeHGlobal(batteryStatusPointer);
                    Marshal.FreeHGlobal(batteryWaitStatusPointer);
                }
            }



            private static bool DeviceIoControl(IntPtr deviceHandle, uint controlCode, ref uint output)
            {
                uint bytesReturned;
                uint junkInput = 0;
                bool retval = Win32.DeviceIoControl(
                    deviceHandle, controlCode, ref junkInput, 0, ref output, (uint)Marshal.SizeOf(output), out bytesReturned, IntPtr.Zero);

                if (!retval)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                        throw Marshal.GetExceptionForHR(errorCode);
                    else
                        throw new Exception(
                            "DeviceIoControl call failed but Win32 didn't catch an error.");
                }

                return retval;
            }

            private static bool DeviceIoControl(
                IntPtr deviceHandle, uint controlCode, IntPtr input, int inputSize, IntPtr output, int outputSize)
            {
                uint bytesReturned;
                bool retval = Win32.DeviceIoControl(
                    deviceHandle, controlCode, input, (uint)inputSize, output, (uint)outputSize, out bytesReturned, IntPtr.Zero);

                if (!retval)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                        throw Marshal.GetExceptionForHR(errorCode);
                    else
                        throw new Exception(
                            "DeviceIoControl call failed but Win32 didn't catch an error.");
                }

                return retval;
            }

            private static IntPtr SetupDiGetClassDevs(Guid guid, Win32.DEVICE_GET_CLASS_FLAGS flags)
            {
                IntPtr handle = Win32.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, flags);

                if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                        throw Marshal.GetExceptionForHR(errorCode);
                    else
                        throw new Exception("SetupDiGetClassDev call returned a bad handle.");
                }
                return handle;
            }

            private static bool SetupDiEnumDeviceInterfaces(
                IntPtr deviceInfoSet, Guid guid, int memberIndex, ref Win32.SP_DEVICE_INTERFACE_DATA deviceInterfaceData)
            {
                bool retval = Win32.SetupDiEnumDeviceInterfaces(
                    deviceInfoSet, IntPtr.Zero, ref guid, (uint)memberIndex, ref deviceInterfaceData);

                if (!retval)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                    {
                        if (errorCode == 259)
                            throw new Exception("SetupDeviceInfoEnumerateDeviceInterfaces ran out of batteries to enumerate.");

                        throw Marshal.GetExceptionForHR(errorCode);
                    }
                    else
                        throw new Exception(
                            "SetupDeviceInfoEnumerateDeviceInterfaces call failed but Win32 didn't catch an error.");
                }
                return retval;
            }

            private static bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet)
            {
                bool retval = Win32.SetupDiDestroyDeviceInfoList(deviceInfoSet);

                if (!retval)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                        throw Marshal.GetExceptionForHR(errorCode);
                    else
                        throw new Exception(
                            "SetupDiDestroyDeviceInfoList call failed but Win32 didn't catch an error.");
                }
                return retval;
            }

            private static bool SetupDiGetDeviceInterfaceDetail(
                IntPtr deviceInfoSet, ref Win32.SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref Win32.SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData, int deviceInterfaceDetailSize)
            {
                //int tmpSize = Marshal.SizeOf(deviceInterfaceDetailData);
                uint reqSize;
                bool retval = Win32.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet, ref deviceInterfaceData, ref deviceInterfaceDetailData, (uint)deviceInterfaceDetailSize, out reqSize, IntPtr.Zero);
                retval = Win32.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, ref deviceInterfaceDetailData, (uint)reqSize, out reqSize, IntPtr.Zero);

                if (!retval)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                        throw Marshal.GetExceptionForHR(errorCode);
                    else
                        throw new Exception(
                            "SetupDiGetDeviceInterfaceDetail call failed but Win32 didn't catch an error.");
                }
                return retval;
            }

            private static IntPtr CreateFile(
                string filename, FileAccess access, FileShare shareMode, FileMode creation, Win32.FILE_ATTRIBUTES flags)
            {
                IntPtr handle = Win32.CreateFile(
                    filename, access, shareMode, IntPtr.Zero, creation, flags, IntPtr.Zero);

                if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != 0)
                        Marshal.ThrowExceptionForHR(errorCode);
                    else
                        throw new Exception(
                            "SetupDiGetDeviceInterfaceDetail call failed but Win32 didn't catch an error.");
                }
                return handle;
            }
        }

        internal static class Win32
        {
            internal static readonly Guid GUID_DEVCLASS_BATTERY = new Guid(0x72631E54, 0x78A4, 0x11D0, 0xBC, 0xF7, 0x00, 0xAA, 0x00, 0xB7, 0xB3, 0x2A);
            internal const uint IOCTL_BATTERY_QUERY_TAG = (0x00000029 << 16) | ((int)FileAccess.Read << 14) | (0x10 << 2) | (0);
            internal const uint IOCTL_BATTERY_QUERY_INFORMATION = (0x00000029 << 16) | ((int)FileAccess.Read << 14) | (0x11 << 2) | (0);
            internal const uint IOCTL_BATTERY_QUERY_STATUS = (0x00000029 << 16) | ((int)FileAccess.Read << 14) | (0x13 << 2) | (0);

            internal const int DEVICE_INTERFACE_BUFFER_SIZE = 120;


            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr SetupDiGetClassDevs(
                ref Guid guid,
                [MarshalAs(UnmanagedType.LPTStr)] string enumerator,
                IntPtr hwnd,
                DEVICE_GET_CLASS_FLAGS flags);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiEnumDeviceInterfaces(
                IntPtr hdevInfo,
                IntPtr devInfo,
                ref Guid guid,
                uint memberIndex,
                ref SP_DEVICE_INTERFACE_DATA devInterfaceData);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiGetDeviceInterfaceDetail(
                IntPtr hdevInfo,
                ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
                ref SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData,
                uint deviceInterfaceDetailDataSize,
                out uint requiredSize,
                IntPtr deviceInfoData);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiGetDeviceInterfaceDetail(
                IntPtr hdevInfo,
                ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
                IntPtr deviceInterfaceDetailData,
                uint deviceInterfaceDetailDataSize,
                out uint requiredSize,
                IntPtr deviceInfoData);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            internal static extern IntPtr CreateFile(
                string filename,
                [MarshalAs(UnmanagedType.U4)] FileAccess desiredAccess,
                [MarshalAs(UnmanagedType.U4)] FileShare shareMode,
                IntPtr securityAttributes,
                [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
                [MarshalAs(UnmanagedType.U4)] FILE_ATTRIBUTES flags,
                IntPtr template);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            internal static extern bool DeviceIoControl(
                IntPtr handle,
                uint controlCode,
                [In] IntPtr inBuffer,
                uint inBufferSize,
                [Out] IntPtr outBuffer,
                uint outBufferSize,
                out uint bytesReturned,
                IntPtr overlapped);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            internal static extern bool DeviceIoControl(
                IntPtr handle,
                uint controlCode,
                ref uint inBuffer,
                uint inBufferSize,
                ref uint outBuffer,
                uint outBufferSize,
                out uint bytesReturned,
                IntPtr overlapped);

            [Flags]
            internal enum DEVICE_GET_CLASS_FLAGS : uint
            {
                DIGCF_DEFAULT = 0x00000001,
                DIGCF_PRESENT = 0x00000002,
                DIGCF_ALLCLASSES = 0x00000004,
                DIGCF_PROFILE = 0x00000008,
                DIGCF_DEVICEINTERFACE = 0x00000010
            }

            [Flags]
            internal enum LOCAL_MEMORY_FLAGS
            {
                LMEM_FIXED = 0x0000,
                LMEM_MOVEABLE = 0x0002,
                LMEM_NOCOMPACT = 0x0010,
                LMEM_NODISCARD = 0x0020,
                LMEM_ZEROINIT = 0x0040,
                LMEM_MODIFY = 0x0080,
                LMEM_DISCARDABLE = 0x0F00,
                LMEM_VALID_FLAGS = 0x0F72,
                LMEM_INVALID_HANDLE = 0x8000,
                LHND = (LMEM_MOVEABLE | LMEM_ZEROINIT),
                LPTR = (LMEM_FIXED | LMEM_ZEROINIT),
                NONZEROLHND = (LMEM_MOVEABLE),
                NONZEROLPTR = (LMEM_FIXED)
            }

            [Flags]
            internal enum FILE_ATTRIBUTES : uint
            {
                Readonly = 0x00000001,
                Hidden = 0x00000002,
                System = 0x00000004,
                Directory = 0x00000010,
                Archive = 0x00000020,
                Device = 0x00000040,
                Normal = 0x00000080,
                Temporary = 0x00000100,
                SparseFile = 0x00000200,
                ReparsePoint = 0x00000400,
                Compressed = 0x00000800,
                Offline = 0x00001000,
                NotContentIndexed = 0x00002000,
                Encrypted = 0x00004000,
                Write_Through = 0x80000000,
                Overlapped = 0x40000000,
                NoBuffering = 0x20000000,
                RandomAccess = 0x10000000,
                SequentialScan = 0x08000000,
                DeleteOnClose = 0x04000000,
                BackupSemantics = 0x02000000,
                PosixSemantics = 0x01000000,
                OpenReparsePoint = 0x00200000,
                OpenNoRecall = 0x00100000,
                FirstPipeInstance = 0x00080000
            }

            internal enum BATTERY_QUERY_INFORMATION_LEVEL
            {
                BatteryInformation = 0,
                BatteryGranularityInformation = 1,
                BatteryTemperature = 2,
                BatteryEstimatedTime = 3,
                BatteryDeviceName = 4,
                BatteryManufactureDate = 5,
                BatteryManufactureName = 6,
                BatteryUniqueID = 7
            }

            [Flags]
            internal enum POWER_STATE : uint
            {
                BATTERY_POWER_ONLINE = 0x00000001,
                BATTERY_DISCHARGING = 0x00000002,
                BATTERY_CHARGING = 0x00000004,
                BATTERY_CRITICAL = 0x00000008
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            internal struct BATTERY_INFORMATION
            {
                public int Capabilities;
                public byte Technology;

                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
                public byte[] Reserved;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
                public byte[] Chemistry;

                public int DesignedCapacity;
                public int FullChargedCapacity;
                public int DefaultAlert1;
                public int DefaultAlert2;
                public int CriticalBias;
                public int CycleCount;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            internal struct SP_DEVICE_INTERFACE_DETAIL_DATA
            {
                public int CbSize;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
                public string DevicePath;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct SP_DEVICE_INTERFACE_DATA
            {
                public int CbSize;
                public Guid InterfaceClassGuid;
                public int Flags;
                public UIntPtr Reserved;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            internal struct BATTERY_QUERY_INFORMATION
            {
                public uint BatteryTag;
                public BATTERY_QUERY_INFORMATION_LEVEL InformationLevel;
                public int AtRate;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            internal struct BATTERY_STATUS
            {
                public POWER_STATE PowerState;
                public uint Capacity;
                public uint Voltage;
                public int Rate;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            internal struct BATTERY_WAIT_STATUS
            {
                public uint BatteryTag;
                public uint Timeout;
                public POWER_STATE PowerState;
                public uint LowCapacity;
                public uint HighCapacity;
            }
        }
    }
}
