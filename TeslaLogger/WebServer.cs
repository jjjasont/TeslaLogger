﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

namespace TeslaLogger
{
    public class WebServer
    {
        private HttpListener listener = null;

        private List<string> AllowedTeslaAPICommands = new List<string>()
        {
            "auto_conditioning_start",
            "auto_conditioning_stop",
            "auto_conditioning_toggle",
            "sentry_mode_on",
            "sentry_mode_off",
            "sentry_mode_toggle"
        };

        public WebServer()
        {
            if (!HttpListener.IsSupported)
            {
                Logfile.Log("HttpListener is not Supported!!!");
                return;
            }
            
            try
            {
                int httpport = Tools.GetHttpPort();
                listener = new HttpListener();
                listener.Prefixes.Add($"http://*:{httpport}/");
                listener.Start();

                Logfile.Log($"HttpListener bound to http://*:{httpport}/");
            }
            catch (HttpListenerException hlex)
            {
                listener = null;
                if (((UInt32)hlex.HResult) == 0x80004005)
                {
                    Logfile.Log("HTTPListener access denied. Check https://stackoverflow.com/questions/4019466/httplistener-access-denied");
                }
                else
                {
                    Logfile.Log(hlex.ToString());
                }
            }
            catch (Exception ex)
            {
                listener = null;
                Logfile.Log(ex.ToString());
            }

            try
            {
                if (listener == null)
                {
                    int httpport = Tools.GetHttpPort();
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://localhost:{httpport}/");
                    listener.Start();

                    Logfile.Log($"HTTPListener only bound to Localhost:{httpport}!");
                }
            }
            catch (HttpListenerException hlex)
            {
                listener = null;
                if (((UInt32)hlex.HResult) == 0x80004005)
                {
                    Logfile.Log("HTTPListener access denied. Check https://stackoverflow.com/questions/4019466/httplistener-access-denied");
                }
                else
                {
                    Logfile.Log(hlex.ToString());
                }
            }
            catch (Exception ex)
            {
                listener = null;
                Logfile.Log(ex.ToString());
            }

            while (true)
            {
                try
                {
                    ThreadPool.QueueUserWorkItem(OnContext, listener.GetContext());
                }
                catch (Exception ex)
                {
                    Logfile.Log(ex.ToString());
                }
            }
        }

        private void OnContext(object o)
        {
            string localpath = "";

            try
            {
                HttpListenerContext context = o as HttpListenerContext;

                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                if (request.Url.LocalPath == null)
                    localpath = "NULL";
                else
                    localpath = request.Url.LocalPath;

                switch (true)
                {
                    // commands for admin UI
                    case bool _ when request.Url.LocalPath.Equals("/getchargingstate"):
                        Getchargingstate(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/setcost"):
                        Setcost(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/getallcars"):
                        GetAllCars(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/setpassword"):
                        SetPassword(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/admin/UpdateElevation"):
                        Admin_UpdateElevation(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/admin/ReloadGeofence"):
                        Admin_ReloadGeofence(request, response);
                        break;
                    // get car values
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/get/[0-9]+/.+"):
                        Get_CarValue(request, response);
                        break;
                    // send car commands
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/command/[0-9]+/.+"):
                        SendCarCommand(request, response);
                        break;
                    // Tesla API debug
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/debug/TeslaAPI/[0-9]+/.+"):
                        Debug_TeslaAPI(request.Url.LocalPath, request, response);
                        break;
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/debug/TeslaLogger/[0-9]+/states"):
                        Debug_TeslaLoggerStates(request, response);
                        break;
                    default:
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        WriteString(response, @"URL Not Found!");
                        break;
                }

            }
            catch (Exception ex)
            {
                Logfile.Log($"Localpath: {localpath}\r\n" + ex.ToString());
            }
        }

        private void SendCarCommand(HttpListenerRequest request, HttpListenerResponse response)
        {
            Match m = Regex.Match(request.Url.LocalPath, @"/get/([0-9]+)/(.+)");
            if (m.Success && m.Groups.Count == 3 && m.Groups[1].Captures.Count == 1 && m.Groups[2].Captures.Count == 1)
            {
                string command = m.Groups[2].Captures[0].ToString();
                int.TryParse(m.Groups[1].Captures[0].ToString(), out int CarID);
                if (command.Length > 0 && CarID > 0)
                {
                    Car car = Car.GetCarByID(CarID);
                    if (car != null)
                    {
                        // check if command is in list of allowed commands
                        if (AllowedTeslaAPICommands.Contains(command))
                        {
                            switch(command)
                            {
                                case "auto_conditioning_start":
                                    WriteString(response, car.webhelper.PostCommand("command/auto_conditioning_start", null).Result);
                                    break;
                                case "auto_conditioning_stop":
                                    WriteString(response, car.webhelper.PostCommand("command/auto_conditioning_stop", null).Result);
                                    break;
                                case "auto_conditioning_toggle":
                                    if (car.currentJSON.current_is_preconditioning)
                                    {
                                        WriteString(response, car.webhelper.PostCommand("command/auto_conditioning_stop", null).Result);
                                    }
                                    else
                                    {
                                        WriteString(response, car.webhelper.PostCommand("command/auto_conditioning_start", null).Result);
                                    }
                                    break;
                                case "sentry_mode_on":
                                    WriteString(response, car.webhelper.PostCommand("command/set_sentry_mode", "{\"on\":true}", true).Result);
                                    break;
                                case "sentry_mode_off":
                                    WriteString(response, car.webhelper.PostCommand("command/set_sentry_mode", "{\"on\":false}", true).Result);
                                    break;
                                case "sentry_mode_toggle":
                                    if (car.webhelper.is_sentry_mode)
                                    {
                                        WriteString(response, car.webhelper.PostCommand("command/set_sentry_mode", "{\"on\":false}", true).Result);
                                    }
                                    else
                                    {
                                        WriteString(response, car.webhelper.PostCommand("command/set_sentry_mode", "{\"on\":true}", true).Result);
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }

        private void SetPassword(HttpListenerRequest request, HttpListenerResponse response)
        {
            Logfile.Log("SetPassword");

            string data;
            using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                data = reader.ReadToEnd();
            }

            NameValueCollection r = HttpUtility.ParseQueryString(data);
            string email = r["email"];
            string password = r["password"];
            int teslacarid = Convert.ToInt32(r["carid"]);
            int id = Convert.ToInt32(r["id"]);

            if (id == -1)
            {
                Logfile.Log("Insert Password");

                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();

                    MySqlCommand cmd = new MySqlCommand("select max(id)+1 from cars", con);
                    int newid = Convert.ToInt32(cmd.ExecuteScalar());

                    cmd = new MySqlCommand("insert cars (id, tesla_name, tesla_password, tesla_carid) values (@id, @tesla_name, @tesla_password, @tesla_carid)", con);
                    cmd.Parameters.AddWithValue("@id", newid);
                    cmd.Parameters.AddWithValue("@tesla_name", email);
                    cmd.Parameters.AddWithValue("@tesla_password", password);
                    cmd.Parameters.AddWithValue("@tesla_carid", teslacarid);
                    cmd.ExecuteNonQuery();

                    Car nc = new Car(newid, email, password, teslacarid, "", DateTime.MinValue);
                }
            }
            else
            {
                Logfile.Log("Update Password ID:" + id);
                int dbID = Convert.ToInt32(id);

                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();

                    MySqlCommand cmd = new MySqlCommand("update cars set tesla_name=@tesla_name, tesla_password=@tesla_password, tesla_carid=@ where id=@id", con);
                    cmd.Parameters.AddWithValue("@id", dbID);
                    cmd.Parameters.AddWithValue("@tesla_name", email);
                    cmd.Parameters.AddWithValue("@tesla_password", password);
                    cmd.Parameters.AddWithValue("@tesla_carid", teslacarid);
                    cmd.ExecuteNonQuery();

                    Car c = Car.GetCarByID(dbID);
                    if (c != null)
                    {
                        c.ExitTeslaLogger("Credentials changed!");
                    }

                    Car nc = new Car(dbID, email, password, teslacarid, "", DateTime.MinValue);
                }
            }
            
        }

        private void Get_CarValue(HttpListenerRequest request, HttpListenerResponse response)
        {
            Match m = Regex.Match(request.Url.LocalPath, @"/get/([0-9]+)/(.+)");
            if (m.Success && m.Groups.Count == 3 && m.Groups[1].Captures.Count == 1 && m.Groups[2].Captures.Count == 1)
            {
                string value = m.Groups[2].Captures[0].ToString();
                int.TryParse(m.Groups[1].Captures[0].ToString(), out int CarID);
                if (value.Length > 0 && CarID > 0)
                {
                    Car car = Car.GetCarByID(CarID);
                    if (car != null)
                    {
                        // TODO
                    }
                }
            }
        }

        private static void Admin_ReloadGeofence(HttpListenerRequest request, HttpListenerResponse response)
        {
            Logfile.Log("Admin: ReloadGeofence ...");
            WebHelper.geofence.Init();
            
            if (request.QueryString.Count == 1 && string.Concat(request.QueryString.GetValues(0)).Equals("html"))
            {
                IEnumerable<string> trs = WebHelper.geofence.sortedList.Select(
                    a => string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                        a.name,
                        a.lat,
                        a.lng, 
                        a.radius,
                        string.Concat(a.specialFlags.Select(
                            sp => string.Format("{0}<br/>",
                            sp.ToString()))
                        ),
                        a.geofenceSource.ToString()
                    )
                );
                WriteString(response, "<html><head></head><body><table border=\"1\">" + string.Concat(trs) + "</table></body></html>");
            }
            else
            {
                WriteString(response, "{\"response\":{\"reason\":\"\", \"result\":true}}");
            }
            WebHelper.UpdateAllPOIAddresses();
            Logfile.Log("Admin: ReloadGeofence done");
        }

        private void Debug_TeslaLoggerStates(HttpListenerRequest request, HttpListenerResponse response)
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "System.DateTime.Now", DateTime.Now.ToString() },
                { "System.DateTime.UtcNow", DateTime.UtcNow.ToString() },
                { "System.DateTime.UnixTime", Tools.ToUnixTime(DateTime.Now).ToString() },
                { "UpdateTeslalogger.lastVersionCheck", UpdateTeslalogger.GetLastVersionCheck().ToString() },
                {
                "TLMemCacheKey.Housekeeping",
                MemoryCache.Default.Get(Program.TLMemCacheKey.Housekeeping.ToString()) != null
                    ? "AbsoluteExpiration: " + ((CacheItemPolicy)MemoryCache.Default.Get(Program.TLMemCacheKey.Housekeeping.ToString())).AbsoluteExpiration.ToString()
                    : "null"
                },
            };

            foreach (Car car in Car.allcars)
            {
                Dictionary<string, string> carvalues = new Dictionary<string, string>
                {
                    { $"Car #{car.CarInDB} GetCurrentState()", car.GetCurrentState().ToString() },
                    { $"Car #{car.CarInDB} GetWebHelper().GetLastShiftState()", car.GetWebHelper().GetLastShiftState().ToString() },
                    { $"Car #{car.CarInDB} GetHighFrequencyLogging()", car.GetHighFrequencyLogging().ToString() },
                    { $"Car #{car.CarInDB} GetHighFrequencyLoggingTicks()", car.GetHighFrequencyLoggingTicks().ToString() },
                    { $"Car #{car.CarInDB} GetHighFrequencyLoggingTicksLimit()", car.GetHighFrequencyLoggingTicksLimit().ToString() },
                    { $"Car #{car.CarInDB} GetHighFrequencyLoggingUntil()", car.GetHighFrequencyLoggingUntil().ToString() },
                    { $"Car #{car.CarInDB} GetHighFrequencyLoggingMode()", car.GetHighFrequencyLoggingMode().ToString() },
                    { $"Car #{car.CarInDB} GetLastCarUsed()", car.GetLastCarUsed().ToString() },
                    { $"Car #{car.CarInDB} GetLastOdometerChanged()", car.GetLastOdometerChanged().ToString() },
                    { $"Car #{car.CarInDB} GetLastTryTokenRefresh()", car.GetLastTryTokenRefresh().ToString() },
                    { "Program.lastSetChargeLimitAddressName",
                        car.GetLastSetChargeLimitAddressName().Equals(string.Empty)
                        ? "&lt;&gt;"
                        : car.GetLastSetChargeLimitAddressName()
                    },
                    { $"Car #{car.CarInDB} GetGoSleepWithWakeup()", car.GetGoSleepWithWakeup().ToString() },
                    { $"Car #{car.CarInDB} GetOdometerLastTrip()", car.GetOdometerLastTrip().ToString() },
                    { $"Car #{car.CarInDB} WebHelper.lastIsDriveTimestamp", car.GetWebHelper().lastIsDriveTimestamp.ToString() },
                    { $"Car #{car.CarInDB} WebHelper.lastUpdateEfficiency", car.GetWebHelper().lastUpdateEfficiency.ToString() },
                };
                string carHTMLtable = "<table>" + string.Concat(carvalues.Select(a => string.Format("<tr><td>{0}</td><td>{1}</td></tr>", a.Key, a.Value))) + "</table>";
                values.Add($"Car #{car.CarInDB}", carHTMLtable);
            }

            /*{
                "TLMemCacheKey.GetOutsideTempAsync",
                MemoryCache.Default.Get(Program.TLMemCacheKey.GetOutsideTempAsync.ToString()) != null
                    ? ((double)MemoryCache.Default.Get(Program.TLMemCacheKey.GetOutsideTempAsync.ToString())).ToString()
                    : "null"
            },*/

            IEnumerable<string> trs = values.Select(a => string.Format("<tr><td>{0}</td><td>{1}</td></tr>", a.Key, a.Value));
            WriteString(response, "<html><head></head><body><table>" + string.Concat(trs) + "</table></body></html>");
        }

        private void Debug_TeslaAPI(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            Match m = Regex.Match(request.Url.LocalPath, @"/debug/TeslaAPI/([0-9]+)/(.+)");
            if (m.Success && m.Groups.Count == 3 && m.Groups[1].Captures.Count == 1 && m.Groups[2].Captures.Count == 1)
            {
                string value = m.Groups[1].Captures[0].ToString();
                int.TryParse(m.Groups[2].Captures[0].ToString(), out int CarID);
                if (value.Length > 0 && CarID > 0)
                {
                    Car car = Car.GetCarByID(CarID);
                    if (car != null && car.GetWebHelper().TeslaAPI_Commands.TryGetValue(path, out string TeslaAPIJSON))
                    {
                        response.AddHeader("Content-Type", "application/json");
                        WriteString(response, TeslaAPIJSON);
                    }
                    else
                    {
                        WriteString(response, "");
                    }
                }
                else
                {
                    WriteString(response, "");
                }
            }
            else
            {
                WriteString(response, "");
            }
        }

        private void Setcost(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Logfile.Log("SetCost");

                string json;

                if (request.QueryString["JSON"] != null)
                {
                    json = request.QueryString["JSON"];
                }
                else
                {
                    using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        json = reader.ReadToEnd();
                    }
                }

                Logfile.Log("JSON: " + json);

                dynamic j = new JavaScriptSerializer().DeserializeObject(json);

                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("update chargingstate set cost_total = @cost_total, cost_currency=@cost_currency, cost_per_kwh=@cost_per_kwh, cost_per_session=@cost_per_session, cost_per_minute=@cost_per_minute, cost_idle_fee_total=@cost_idle_fee_total, cost_kwh_meter_invoice=@cost_kwh_meter_invoice  where id= @id", con);

                    if (DBHelper.DBNullIfEmptyOrZero(j["cost_total"]) is DBNull && DBHelper.IsZero(j["cost_per_session"]))
                    {
                        cmd.Parameters.AddWithValue("@cost_total", 0);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@cost_total", DBHelper.DBNullIfEmptyOrZero(j["cost_total"]));
                    }

                    cmd.Parameters.AddWithValue("@cost_currency", DBHelper.DBNullIfEmpty(j["cost_currency"]));
                    cmd.Parameters.AddWithValue("@cost_per_kwh", DBHelper.DBNullIfEmpty(j["cost_per_kwh"]));
                    cmd.Parameters.AddWithValue("@cost_per_session", DBHelper.DBNullIfEmpty(j["cost_per_session"]));
                    cmd.Parameters.AddWithValue("@cost_per_minute", DBHelper.DBNullIfEmpty(j["cost_per_minute"]));
                    cmd.Parameters.AddWithValue("@cost_idle_fee_total", DBHelper.DBNullIfEmpty(j["cost_idle_fee_total"]));
                    cmd.Parameters.AddWithValue("@cost_kwh_meter_invoice", DBHelper.DBNullIfEmpty(j["cost_kwh_meter_invoice"]));

                    cmd.Parameters.AddWithValue("@id", j["id"]);
                    int done = cmd.ExecuteNonQuery();

                    Logfile.Log("SetCost OK: " + done);
                    WriteString(response, "OK");
                }
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                WriteString(response, "ERROR");
            }
        }

        private void Getchargingstate(HttpListenerRequest request, HttpListenerResponse response)
        {
            string id = request.QueryString["id"];
            string responseString = "";

            try
            {
                Logfile.Log("HTTP getchargingstate");                
                DataTable dt = new DataTable();
                MySqlDataAdapter da = new MySqlDataAdapter("SELECT chargingstate.*, lat, lng, address, charging.charge_energy_added as kWh FROM chargingstate join pos on chargingstate.pos = pos.id join charging on chargingstate.EndChargingID = charging.id where chargingstate.id = @id", DBHelper.DBConnectionstring);
                da.SelectCommand.Parameters.AddWithValue("@id", id);
                da.Fill(dt);

                responseString = dt.Rows.Count > 0 ? Tools.DataTableToJSONWithJavaScriptSerializer(dt) : "not found!";
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
            }

            WriteString(response, responseString);
        }

        private void GetAllCars(HttpListenerRequest request, HttpListenerResponse response)
        {
            string responseString = "";

            try
            {
                DataTable dt = new DataTable();
                MySqlDataAdapter da = new MySqlDataAdapter("SELECT id, display_name, tasker_hash, model_name, vin, tesla_name, tesla_carid FROM cars order by display_name", DBHelper.DBConnectionstring);
                da.Fill(dt);

                responseString = dt.Rows.Count > 0 ? Tools.DataTableToJSONWithJavaScriptSerializer(dt) : "not found!";
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
            }

            WriteString(response, responseString);
        }

        private static void WriteString(HttpListenerResponse response, string responseString)
        {
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }

        private void Admin_UpdateElevation(HttpListenerRequest request, HttpListenerResponse response)
        {
            int from = 1;
            int to = 1;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("Select max(id) from pos", con);
                    MySqlDataReader dr = cmd.ExecuteReader();
                    if (dr.Read() && dr[0] != DBNull.Value)
                    {
                        int.TryParse(dr[0].ToString(), out to);
                    }
                    con.Close();
                }
            }
            catch (Exception) { }
            Logfile.Log($"Admin: UpdateElevation ({from} -> {to}) ...");
            WriteString(response, $"Admin: UpdateElevation ({from} -> {to}) ...");
            DBHelper.UpdateTripElevation(from, to, "/admin/UpdateElevation");
            Logfile.Log("Admin: UpdateElevation done");
        }
    }
}
