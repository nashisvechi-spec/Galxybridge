using System.Net.NetworkInformation;

namespace GalaxyBridge.Windows;

internal static class ConnectionDiagnostics
{
    public static string Build(bool connected,bool paused,bool controlReady,string destination)
    {
        List<string> report=["Galaxy Bridge · проверка подключения",DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            "Связь: "+(paused?"остановлена в приложении":connected?"установлена":"не установлена"),
            "Управление телефоном: "+(controlReady?"готово":"нужны подключение, разрешения и разблокированный экран")];
        try {
            var interfaces=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).ToArray();
            bool ipv4=false,vpn=false;
            foreach(var network in interfaces) {
                var addresses=network.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork).Select(a=>a.Address.ToString()).ToArray();
                if(addresses.Length==0) continue;ipv4=true;string label=(network.Name+" "+network.Description).ToLowerInvariant();
                vpn|=network.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp || new[]{"vpn","wireguard","wintun","tailscale","tap-"}.Any(label.Contains);
                report.Add("Сеть: "+network.Name+" · "+string.Join(", ",addresses));
            }
            if(!ipv4) report.Add("Не найден IPv4-адрес локальной сети. Подключите ПК к Wi-Fi или Ethernet.");
            if(vpn) report.Add("Обнаружен возможный VPN/туннель. Проверьте разрешение доступа к локальной сети в VPN.");
        } catch(NetworkInformationException) { report.Add("Сведения о сетевых адаптерах недоступны."); }
        try {
            Directory.CreateDirectory(destination);string probe=Path.Combine(destination,".galaxybridge-probe-"+Guid.NewGuid().ToString("N"));
            try { using(FileStream stream=new(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)) stream.WriteByte(0);report.Add("Папка приёма: запись доступна."); }
            finally { if(File.Exists(probe)) File.Delete(probe); }
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException) { report.Add("Папка приёма: запись недоступна. Выберите другую папку и проверьте место на диске."); }
        if(!connected) report.Add("Следующий шаг: запустите приложение на телефоне, подключитесь к сохранённому ПК или отсканируйте QR. Проверьте общую сеть и разрешение Galaxy Bridge в брандмауэре Windows (TCP 38271, UDP 38272). Гостевая сеть может изолировать устройства.");
        report.Add("Проверка не меняет брандмауэр или VPN. Наличие туннеля определяется по свойствам адаптера и не доказывает блокировку.");
        return string.Join(Environment.NewLine+Environment.NewLine,report);
    }
    public static void Show(Form owner,string report)
    {
        Form window=new() { Text="Диагностика · Galaxy Bridge",Icon=AppIcon.Value,Font=owner.Font,Size=new Size(760,620),MinimumSize=new Size(420,320),StartPosition=FormStartPosition.CenterParent };
        TextBox text=new() { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=report };
        BridgeButton copy=BridgeTheme.Button("Скопировать отчёт");copy.Dock=DockStyle.Bottom;
        copy.Click+=(_,_)=> { try { Clipboard.SetText(report); }catch(System.Runtime.InteropServices.ExternalException) { MessageBox.Show(window,"Буфер занят. Повторите копирование."); } };
        window.Controls.Add(text);window.Controls.Add(copy);window.Shown+=(_,_)=>BridgeTheme.FitWindow(window);BridgeTheme.Apply(window);window.Show(owner);
    }
}
