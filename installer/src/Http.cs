// Nessafy installer: HTTPS downloads.

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace Nessafy.Installer
{
    static class Http
    {
        static Http()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
        }

        public static byte[] Get(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Nessafy-Installer";
            request.Accept = url.Contains("api.github.com") ? "application/vnd.github+json" : "application/octet-stream";
            request.Timeout = 60000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        public static Dictionary<string, object> GetJson(string url)
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(Get(url)));
        }
    }
}
