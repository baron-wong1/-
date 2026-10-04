using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace InvoiceAssistant
{
    [DataContract]
    public class Session
    {
        [DataMember] public string Person { get; set; } = "";
        [DataMember] public string Month { get; set; } = "";
        [DataMember] public List<Ticket> Rows { get; set; } = new List<Ticket>();
        [DataMember] public List<string> Outputs { get; set; } = new List<string>();
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvoiceAssistantLight");
        static string FilePath => Path.Combine(Root, "session.json");
        public static Session Load()
        {
            if (!File.Exists(FilePath)) return new Session();
            using (var stream = File.OpenRead(FilePath)) return (Session)new DataContractJsonSerializer(typeof(Session)).ReadObject(stream);
        }
        public void Save()
        {
            Directory.CreateDirectory(Root); var temporary = FilePath + ".tmp";
            using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(Session)).WriteObject(stream, this);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
        }
    }
}
