using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceProcess;
using System.IO.Ports;
using System.Reflection;
using System.Text.RegularExpressions;

// offered to the public domain for any use with no restriction
// and also with no warranty of any kind, please enjoy. - David Jeske. 

// simple HTTP explanation
// http://www.jmarshall.com/easy/http/

// права админа на программу, как сделать, если что https://msdn.microsoft.com/ru-ru/library/bb383802.aspx
/*Что бы приложение всегда запускалось с правами администратора нужно:

Правой кнопкой мыши по проекту
Добавить -> Создать элемент
В списке выбрать "файл манифеста приложения"
Далее в этом файле нужно заменить строчку

<requestedExecutionLevel>

на
 
<requestedExecutionLevel level="asInvoker" /> 
*/

namespace Bend.Util {
    //проверка на ввод данных
    public class formvalidation
    {
        //Здесь храняться все данные для проверки и какием к ним правила применить
        static Hashtable MasValidation = new Hashtable();

        //функция которая запускает проверку 
        public static bool Start(HttpProcessor p)
        {   //передаем управление классом переменной о
            Type MyClass = typeof(formvalidation);
            object o = Activator.CreateInstance(MyClass);
          
            foreach (DictionaryEntry s in MasValidation)
            {
                //Узнаем какие правила будут использоваться 
                string[] MasMethods = s.Value.ToString().Split('|');
                
                foreach (string rule in MasMethods)
                {
                    string[] ParseRule = rule.TrimEnd(']').Split('[');
                    //запускаем нужный метод который был выбран для проверки 
                    MethodInfo info = typeof(formvalidation).GetMethod(ParseRule[0], BindingFlags.Instance | BindingFlags.NonPublic);    
                     bool Out;
                  if (ParseRule.Length >1)
                      Out = (bool)info.Invoke(o ,new object[] {p.InputPOST((string)s.Key), ParseRule[1]});
                    else
                      Out = (bool)info.Invoke(o ,new object[] {p.InputPOST((string)s.Key)});
                  
                  if (!Out)
                  {
                      MasValidation.Clear();
                      return false;
                  }
                }
            }
            return true;
        }

        //добавление в память текста и какими методами его проверять
        public static void add(string text, string rules)
        {
            MasValidation.Add(text, rules);
        }

        //Проверка на целочисленное
        private bool num(string num)
        {
            Regex rgx = new Regex(@"^[0-9]+$", RegexOptions.IgnoreCase);
            return rgx.IsMatch(num);
        }

        //Проверка на буквы
        private bool alpha(string text)
        {
            Regex rgx = new Regex(@"^[A-zА-я]+$", RegexOptions.IgnoreCase);
            return rgx.IsMatch(text);
        }

        //Проверка на пустоту
        private bool required(string text = "")
        {
            return (text == "" || text == "null") ? false : true;
        }

        //Проверка на уникальность в БД
        private bool is_unique(string text = "", string bd = "")
        {
            string[] ParsingBD = bd.Split('.');
            return (HttpServer.connect.select("Select `" + ParsingBD[1] + "` from `" + ParsingBD[0] + "` Where `" + ParsingBD[1] + "`='" + text + "';"))
                ? false : true;
        }
    }
    //отправка смс
    public static class SendSMS
    {
        public static SerialPort port;

        public static void Send(string smstext, string number, string com)
        {
            
            port = new SerialPort();
            port.PortName = com;

            Console.WriteLine("Отправка сообщения СМС");

            OpenPort(com);
            bool result;
            result = sendSMS(smstext, number);

            if (result == true)
            {
                Console.WriteLine("Сообщение отправлено успешно");
            }
            else
            {
                Console.WriteLine("Произошла ошибка при отправке");
            }
            Console.ReadLine();

            port.Close();
        }

        private static bool sendSMS(string textsms, string telnumber)
        {
            if (!port.IsOpen) return false;

            try
            {
                System.Threading.Thread.Sleep(500);
                port.WriteLine("AT\r\n"); // означает "Внимание!" для модема 
                System.Threading.Thread.Sleep(500);

                port.Write("AT+CMGF=0\r\n"); // устанавливается цифровой режим PDU для отправки сообщений
                System.Threading.Thread.Sleep(500);
            }
            catch
            {
                return false;
            }

            try
            {
                telnumber = telnumber.Replace("-", "").Replace(" ", "").Replace("+", "");

                // 01 это PDU Type или иногда называется SMS-SUBMIT. 01 означает, что сообщение передаваемое, а не получаемое 
                // цифры 00 это TP-Message-Reference означают, что телефон/модем может установить количество успешных сообщений автоматически
                // telnumber.Length.Tostring("X2") выдаст нам длинну номера в 16-ричном формате
                // 91 означает, что используется международный формат номера телефона
                telnumber = "01" + "00" + telnumber.Length.ToString("X2") + "91" + EncodePhoneNumber(telnumber);

                textsms = stringToUCS2(textsms);
                // 00 означает, что формат сообщения неявный. Это идентификатор протокола. Другие варианты телекс, телефакс, голосовое сообщение и т.п.
                // 08 означает формат UCS2 - 2 байта на символ. Он проще, так что рассмотрим его.
                // если вместо 08 указать 18, то сообщение не будет сохранено на телефоне. Получится flash сообщение
                string leninByte = (textsms.Length / 2).ToString("X2");
                textsms = telnumber + "00" + "08" + leninByte + textsms;

                // посылаем команду с длинной сообщения - количество октет в десятичной системе. то есть делим на два количество символов в сообщении
                // если октет неполный, то получится в результате дробное число. это дробное число округляем до большего
                double lenMes = textsms.Length / 2;
                port.Write("AT+CMGS=" + (Math.Ceiling(lenMes)) + "\r\n");
                System.Threading.Thread.Sleep(500);

                // номер sms-центра мы не указываем, считая, что практически во всех SIM картах он уже прописан
                // для того, чтобы было понятно, что этот номер мы не указали добавляем к нашему сообщению в начало 2 нуля
                // добавляем именно ПОСЛЕ того, как подсчитали длинну сообщения
                textsms = "00" + textsms;

                port.Write(textsms + char.ConvertFromUtf32(26) + "\r\n");
                System.Threading.Thread.Sleep(500);
            }
            catch
            {
                return false;
            }

            try
            {
                string recievedData;
                recievedData = port.ReadExisting();

                if (recievedData.Contains("ERROR"))
                {
                    return false;
                }

            }
            catch { }

            return true;
        }




        private static void OpenPort(string com)
        {

            port.BaudRate = 2400; // еще варианты 4800, 9600, 28800 или 56000
            port.DataBits = 7; // еще варианты 8, 9

            port.StopBits = StopBits.One; // еще варианты StopBits.Two StopBits.None или StopBits.OnePointFive         
            port.Parity = Parity.Odd; // еще варианты Parity.Even Parity.Mark Parity.None или Parity.Space

            port.ReadTimeout = 500; // еще варианты 1000, 2500 или 5000 (больше уже не стоит)
            port.WriteTimeout = 500; // еще варианты 1000, 2500 или 5000 (больше уже не стоит)

            //port.Handshake = Handshake.RequestToSend;
            //port.DtrEnable = true;
            //port.RtsEnable = true;
            //port.NewLine = Environment.NewLine;

            port.Encoding = Encoding.GetEncoding("windows-1251");

            // незамысловатая конструкция для открытия порта
            if (port.IsOpen)
                port.Close();
            try
            {
                port.Open();
            }
            catch { }

        }



        // перекодирование номера телефона для формата PDU
        public  static string EncodePhoneNumber(string PhoneNumber)
        {
            string result = "";
            if ((PhoneNumber.Length % 2) > 0) PhoneNumber += "F";

            int i = 0;
            while (i < PhoneNumber.Length)
            {
                result += PhoneNumber[i + 1] + PhoneNumber[i];
                i += 2;
            }
            return result.Trim();
        }


        // перекодирование текста смс в UCS2 
        public static string stringToUCS2(string str)
        {
            UnicodeEncoding ue = new UnicodeEncoding();
            byte[] ucs2 = ue.GetBytes(str);

            int i = 0;
            while (i < ucs2.Length)
            {
                byte b = ucs2[i + 1];
                ucs2[i + 1] = ucs2[i];
                ucs2[i] = b;
                i += 2;
            }
            return BitConverter.ToString(ucs2).Replace("-", "");
        }


    }
    

    //Сессии
    public class Session : HttpServer
    {
        public enum RulesType : int
        {
            admin = 0,
            moderator = 1,
            user = 2
        };

        //здесь ид из cookie
        string[] id;
        //здесь хранятся сессии
        Hashtable MasSession = new Hashtable();
        //начало сессии
        public Session(HttpProcessor p)
        {
            delsession_time();
            //если не было передано Cookie то создаем новое
            if (!p.httpHeaders.ContainsKey("Cookie"))
            {
                //Создание сессии
                create_session(p);
            } else {
                //проверка на ид в куках если есть то создаем сессию
                if (p.httpHeaders["Cookie"].ToString().IndexOf("id=") < 0)
                {
                    create_session(p);
                }
                else
                {
                    //вытаскиваем ид
                    id = p.httpHeaders["Cookie"].ToString().Split(new string[] { "id=" }, StringSplitOptions.RemoveEmptyEntries);
                }
            }

            //если в бд есть инфа про сессию то достаем, если нет заносим
            if (connect.select("select `data` from `sessions` where `id`='" + id[0] + "';"))
            {
                parsing_data();
            }
            else
            {
                create_session(p);
            }
        }

        //Создание сессии
        private void create_session(HttpProcessor p)
        {
            do
            {
                id = new string[1];
                id[0] = Simbol(46);
            }
            while (connect.insert_update("INSERT INTO `sessions` (`id`, `ip_address`, `timestamp`) VALUES(@0, '0.0.0.0', now());", new string[] {id[0]}) == false);

            p.HTML.Header.Add("Set-Cookie:", "id=" + id[0]);

        }

        public string item(string field)
        {
            return (MasSession.ContainsKey(field)) ? MasSession[field].ToString() : "null";
        }

        public void delsession_time()
        {
            connect.insert_update("DELETE FROM `sessions` WHERE TIMEDIFF(now(), `timestamp`) > TIME(@0);", new string[] { "00:15:00" });
        }

        public void exit()
        {
            connect.insert_update("DELETE FROM `sessions` WHERE `id`=@0;", new string[] { MasSession["id"].ToString()});
        }


        //добавление данных в сессию
        public void add(string field, string value)
        {
            if (MasSession.ContainsKey(field))
            {
                MasSession[field] = value;
            }
            else
            {
                MasSession.Add(field,value);
            }
            
        }

        //проверяет на доступ к ресурсу 
        public bool rules(RulesType rule)
        {
            string TempStr = this.item("rules");
            if (TempStr != "null")
                return (TempStr.Substring((int)rule, 1).ToString() == "0") ?
                false : true;
            return false;
                
        }


        //удалить данные из сессии
        public bool del(string field)
        {
            if (MasSession.ContainsKey(field))
            {
                MasSession.Remove(field);
                return true;
            }

            return false;
        }

        //отправка на хранение
        public void push()
        {
            string str="";

            foreach (DictionaryEntry s in MasSession)
            {
                if (s.Key.ToString().IndexOf("HTTP") < 0 && s.Key.ToString().IndexOf("id") < 0)
                    str += s.Key + "=" + s.Value + ";";
            }

            connect.insert_update("UPDATE `sessions` set `timestamp`= now(), `data`=@0;", new string[] { str });

        }
         
        //парсинг данных из mysql  
        void parsing_data()
        {
            MasSession.Add("id", id[0]);
            while (connect.MyReader.Read())// Читаем
            {
                string[] TempData = connect.MyReader.GetValue(0).ToString().Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string s in TempData)
                {
                    string[] TempData2 = s.Split('=');
                    MasSession.Add(TempData2[0], TempData2[1]);
                }
            }
 
        }
    }

    //Проверка и скачивание
    public class Download
    {
        public void download()
        {
            WebClient myWebClient = new WebClient();
            //проверка есть ли FreeRADIUS
            if (!Directory.Exists(@"C:\FreeRADIUS.net")) 
                {
                    //создание директории
                    Directory.CreateDirectory(@".\FreeRADIUS");
                    //скачивание в созданную директорию
                    Console.WriteLine("Загружаю FreeRADIUS... Подождите!");
                    myWebClient.DownloadFile("http://www.freeradius.net/Downloads/FreeRADIUS.net-1.1.7-r0.0.2.exe", @".\FreeRADIUS\FreeRADIUS.net-1.1.7-r0.0.2.exe");
                    Console.WriteLine("Загрузка FreeRADIUS успешно завершена!");
                }

            //проверка есть ли MySQL
            if (!Directory.Exists(@"C:\Program Files\MySQL") && !Directory.Exists(@"C:\Program Files(x86)\MySQL"))
                {
                    //создание директории
                    Directory.CreateDirectory(@".\MySQL");
                    //скачивание в созданную директорию
                    Console.WriteLine("Загружаю MySQL... Подождите!");
                    myWebClient.DownloadFile("http://www.mysql.ru/download/files/mysql-5.5.23-win32.msi", @".\MySQL\MySQLmysql-5.5.23-win32.msi");
                    Console.WriteLine("Загрузка MySQL успешно завершена!");
                }
        }
    }

    public class Services
    {
        public void services()
        {
            ServiceController[] scServices;

            Download down = new Download();

            //----------------------MySQL------------------------------------

            if (Directory.Exists(@"C:\Program Files\MySQL") || Directory.Exists(@"C:\Program Files(x86)\MySQL"))
            {
                Console.WriteLine("MySQL уже установлен на вашем компьютере! Запускаю службу...");
                scServices = ServiceController.GetServices();
                ServiceController scm = new ServiceController("MySQL");
                if (scm.Status == ServiceControllerStatus.Stopped)
                {
                    scm.Start();
                }
            }
            else down.download(); 

            //--------------------------FreeRADIUS--------------------------------------------------

            if (Directory.Exists(@"C:\FreeRADIUS.net")) 
            {
                Console.WriteLine("FreeRADIUS уже установлен на вашем компьютере! Запускаю службу...");
                scServices = ServiceController.GetServices();
                ServiceController scf = new ServiceController("FreeRADIUS.net");
                if (scf.Status == ServiceControllerStatus.Stopped)
                {
                    scf.Start();
                }
            }
            else down.download(); 
        }
    }


    public class MySQLCon
    {
        string MySQL_name = "work_db";
        string MySQL_host = "78.24.222.222";
        string MySQL_port = "3306";
        string MySQL_uid = "root";
        string MySQL_pwd = "12345";
        public MySqlDataReader MyReader;
        MySqlConnection Connection;
        MySqlCommand Query;

        private bool conn()
        {
            Query = new MySqlCommand();
            // Создаем соединение.
            Connection = new MySqlConnection("Database=" + MySQL_name + "; Data Source=" + MySQL_host + ";Port=" + MySQL_port + ";User Id=" + MySQL_uid + ";Password=" + MySQL_pwd + ";");
            Query.Connection = Connection; // Присвоим объекту только что созданное соединение
                try
                {
                    Console.WriteLine("Соединяюсь с сервером базы данных...");
                    Connection.Open();// Соединяемся
                }
                catch (Exception SSDB_Exception)
                {
                    // Ошибка - выходим
                    Console.WriteLine("Проверьте настройки соединения, не могу соединиться с базой данных!\nОшибка: " + SSDB_Exception.Message);
                    return false;
                }

            Console.WriteLine("OK");
            return true;
        }
        
        public bool select(string QueryStr)
        {
            try
            {
                if (MyReader != null) if (!MyReader.IsClosed) MyReader.Close();
                if (Connection == null || Connection.State != System.Data.ConnectionState.Open)
                    conn();
                Query.CommandText = QueryStr;
                MyReader = Query.ExecuteReader();// Запрос, подразумевающий чтение данных из таблиц.
                //Query.Dispose();
                return MyReader.HasRows;
            }
            catch (Exception SSDB_Exception)
            {
                // Ошибка - выходим
                Console.WriteLine("Проверьте целостность базы данных!\nОшибка: " + SSDB_Exception.Message);
                return false;
            }
        }

        public bool insert_update(string QueryStr, string[] val)
        {
            try
            {
                if (MyReader != null) if (!MyReader.IsClosed) MyReader.Close();
                if (Connection == null || Connection.State != System.Data.ConnectionState.Open)
                    conn();
                Query.CommandText = QueryStr;
                Query.Prepare();

                for (int j = 0; j <= val.Length - 1; j++)
                    {
                        Query.Parameters.AddWithValue("@" + j, val[j]);
                    }

                    bool outt = (Query.ExecuteNonQuery() > 0) ? true : false;
                //Query.Dispose();
                    Query.Parameters.Clear();
                return outt;
            }
            catch (MySqlException SSDB_Exception)
            {
                // Ошибка - выходим
                Console.WriteLine("Проверьте целостность базы данных!\nОшибка: " + SSDB_Exception.Message);
                return false;
            }
            
        }

        public void close()
        {
            try
            {
                Connection.Close();
                MyReader.Close();
            }
            catch (Exception)
            {
            }
        }
// ////////////////////////////////////////////////////////////////////
    /*          Console.WriteLine("\nЧтение данных...\nID - Модель авто - Привод - Руль - Коробка передач");
                Query.CommandText = "SELECT * FROM " + MySQL_tbname + ";";
                MySqlDataReader MyReader = Query.ExecuteReader();// Запрос, подразумевающий чтение данных из таблиц.
                while (MyReader.Read())// Читаем
                {
                     // Каждое значение вытягиваем с помощью MySqlDataReader.GetValue(<номер значения в выборке>)
                    Console.WriteLine("{0} - {1} - {2} - {3} - {4}", MyReader.GetValue(0), MyReader.GetValue(1), MyReader.GetValue(2), MyReader.GetValue(3), MyReader.GetValue(4));
                }
/////////////////////////////////////////////////////////////////////////
Выгружаем ресурсы, закрываем соединение:
MyReader.Close();
Query.Dispose();
Connection.Close();
        
    Query.CommandText = "INSERT INTO " + MySQL_tbname + " VALUES(NULL, '" + models[i] + "','" + drives[i] + "','" + rudders[i] + "','" + gearboxes[i] + "');";
    Query.ExecuteNonQuery();

        */
/////////////////////////////////////////////////


    }

    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer srv;
        private Stream inputStream;
        public StreamWriter outputStream;
         //Маркер который отмечается чтобы понять будет перенаправление или нет.
        public struct MarkerRedirectType
        {
            public string url;
            public bool status;
        }

        public MarkerRedirectType  MarkerRedirect;

        //Здесь буду храниться данные переданные браузером серверу POST & GET
        public Hashtable MasInputPost = new Hashtable();
        public Hashtable MasInputGet = new Hashtable();
        //Структура шаблонов
        public struct HTMLBody
        {
            public Hashtable Header;
            public Hashtable Body;
        }

        //Переменная шаблонов
        public HTMLBody HTML;
        public string http_method;
        public string http_url;
        public string http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();


        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv) {
            this.socket = s;
            this.srv = srv;                   
        }
        

        private string streamReadLine(Stream inputStream) {
            int next_char;
            string data = "";
            while (true) {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }            
            return data;
        }
        public void process() {                        
            // we can't use a StreamReader for input, because it buffers up extra data on us inside it's
            // "processed" view of the world, and we want the data raw after the headers
            inputStream = new BufferedStream(socket.GetStream());

            // we probably shouldn't be using a streamwriter for all output from handlers either
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try {
                parseRequest();
                readHeaders();
                //Инициализируем Header и Body
                HTML.Header = new Hashtable();
                HTML.Body = new Hashtable();

                if (http_method.Equals("GET")) {
                    handleGETRequest();
                } else if (http_method.Equals("POST")) {
                    handlePOSTRequest();
                }
            } catch (Exception e) {
                Console.WriteLine("Exception: " + e.ToString());
                outputStream.Write("HTTP/1.0 404 File not found\n");
                outputStream.Write("Connection: close\n\n");
            }
            outputStream.Flush();
            // bs.Flush(); // flush any remaining output
            MasInputGet = null; MasInputPost = null;
            inputStream = null; outputStream = null; // bs = null;            
            socket.Close();             
        }

        public void parseRequest() {
            string request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3) {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];

            Console.WriteLine("starting: " + request);
        }

        public void readHeaders() {
            Console.WriteLine("readHeaders()");
            string line;
            while ((line = streamReadLine(inputStream)) != null) {
                if (line.Equals("")) {
                    Console.WriteLine("got headers");
                    return;
                }
                
                int separator = line.IndexOf(':');
                if (separator == -1) {
                    throw new Exception("invalid http header line: " + line);
                }
                string name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' ')) {
                    pos++; // strip any spaces
                }
                    
                string value = line.Substring(pos, line.Length - pos);
 
                Console.WriteLine("header: {0}:{1}",name,value);
                httpHeaders[name] = value;
            }
        }

        public void handleGETRequest() {
            //получение GET данных от пользователя
            GETDATA(this);
            srv.route(this);
        }

        private const int BUF_SIZE = 4096;
        public void handlePOSTRequest() {
            // this post data processing just reads everything into a memory stream.
            // this is fine for smallish things, but for large stuff we should really
            // hand an input stream to the request processor. However, the input stream 
            // we hand him needs to let him see the "end of the stream" at this content 
            // length, because otherwise he won't know when he's seen it all! 

            Console.WriteLine("get post data start");
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length")) {
                 content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                 if (content_len > MAX_POST_SIZE) {
                     throw new Exception(
                         string.Format("POST Content-Length({0}) too big for this simple server",
                           content_len));
                 }
                 byte[] buf = new byte[BUF_SIZE];              
                 int to_read = content_len;
                 while (to_read > 0) {  
                     Console.WriteLine("starting Read, to_read={0}",to_read);

                     int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                     Console.WriteLine("read finished, numread={0}", numread);
                     if (numread == 0) {
                         if (to_read == 0) {
                             break;
                         } else {
                             throw new Exception("client disconnected during post");
                         }
                     }
                     to_read -= numread;
                     ms.Write(buf, 0, numread);
                 }
                 ms.Seek(0, SeekOrigin.Begin);
            }
            //Получение GET и POST данных от пользователя
            POSTDATA(new StreamReader(ms));
            GETDATA(this);
            Console.WriteLine("get post data end");
            srv.route(this);

        }

        //Процедура вывода POST данных
        private void POSTDATA(StreamReader inputData)
        {
            string str = inputData.ReadToEnd();
            if (str != "")
            {
            //Разделяем POST на ключ=значение
            string[] data = str.Split('&');
            //Переводим данные POST в ключ -> значение
            foreach (string i in data)
            {
                string[] InputPostTemp = i.Split('=');
                MasInputPost.Add(InputPostTemp[0], Uri.UnescapeDataString(InputPostTemp[1]));
            }
            }
        }

        //Процедура вывода GET данных
        private void GETDATA(HttpProcessor inputData)
        {
            string [] data;
            //Разделяем POST на ключ=значение
            if (inputData.http_url.IndexOf('?') != -1)
            {
                data = inputData.http_url.Substring(inputData.http_url.IndexOf('?') + 1).Split('&');
                //Переводим данные POST в ключ -> значение
                foreach (string i in data)
                {
                    string[] InputPostTemp = i.Split('=');
                    MasInputGet.Add(InputPostTemp[0], Uri.UnescapeDataString(InputPostTemp[1]));
                }
            }
        }

        //Собираем и отправляем HTML результат пользователю
        public void SendToUsers(string nametemplate)
        {
            //переменный для собора данных на отправку заголовков и HTML
            string CompilHeader = "";
            string Compiltemplate = "";

            //Если был выставлен Redirect то удалить все заголовки и HTML изменения и сделать перенаправление
            if (MarkerRedirect.status)
            {
                HTML.Header.Clear();
                HTML.Body.Clear();
                HTML.Header.Add("HTTP/1.1", "301 Moved Permanenrly");
                HTML.Header.Add("Location: ", MarkerRedirect.url);
            }
            else
            {
                HTML.Header.Add("HTTP/1.1", "200 OK");
                //Добавляем в HTMl голову,тело, низ
                Compiltemplate += System.IO.File.ReadAllText(@"C:\Project\Access\d2\template/header.html");
                //проверяем на существование вызываемого шаблона страницы
                if (System.IO.File.Exists(@"C:\Project\Access\d2\template/" + nametemplate + ".html"))
                    Compiltemplate += System.IO.File.ReadAllText(@"C:\Project\Access\d2\template/" + nametemplate + ".html");
                else Compiltemplate += "Нет такого файла";
                
                Compiltemplate += System.IO.File.ReadAllText(@"C:\Project\Access\d2\template/footer.html");

                HTML.Body.Add("httppath","http://localhost:8080");
                //Редактируем HTML заменой переменных данными
                foreach (DictionaryEntry s in HTML.Body)
                {
                    Compiltemplate = Compiltemplate.Replace("{" + s.Key.ToString() + "}", s.Value.ToString());
                }

                //если длина больше и заголовок не равено 0 то добавить длину в заголовок
                if (Compiltemplate.Length > 0 & HTML.Header.Count != 0)
                    HTML.Header.Add("Content-Length:", Compiltemplate.Length);
            }

            //Считываем ключ и значение и собираем заголовок
            foreach (DictionaryEntry s in HTML.Header)
            {
                if (s.Key.ToString().IndexOf("HTTP") < 0)
                {
                    CompilHeader += s.Key + " " + s.Value + "\n";
                }
                else
                {
                    CompilHeader = s.Key + " " + s.Value + "\n" + CompilHeader;
                }
            }

            //Отправка данных
            outputStream.Write(CompilHeader + "\n" + Compiltemplate);           
        }

        //проверка на доступность переменной из MasInputPost если ее нет возвращает "null"
        public string InputPOST(string name)
        {
            return (MasInputPost.ContainsKey(name)) ? MasInputPost[name].ToString() : "null";
        }

        //проверка на доступность переменной из MasInputGet если ее нет возвращает "null"
        public string InputGET(string name)
        {
            return (MasInputGet.ContainsKey(name)) ? MasInputGet[name].ToString() : "null";
        }

        //функция перенаправления 
        public void redirect(string url)
        {
            MarkerRedirect.status = true;
            MarkerRedirect.url = url;
        }
    }

    public class HttpServer {
        protected int port;
        TcpListener listener;
        bool is_active = true;
        public static MySQLCon connect = new MySQLCon(); //переменная соединения с базой
        public static Session Sessions; 
       
        public void HttpServerP(int port) {
            this.port = port;
        }

        public void listen() {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            while (is_active) {                
                TcpClient s = listener.AcceptTcpClient();
                HttpProcessor processor = new HttpProcessor(s, this);
                Thread thread = new Thread(new ThreadStart(processor.process));
                thread.Start();
                Thread.Sleep(1);
            }
        }

        //функция для созднии рандоманой строки
        public string Simbol(int count)
        {
            string str = "";
            Random rnd = new Random();
            for (int i = 0; i < count; i++)
            {
                char ch = Convert.ToChar(rnd.Next(97, 122));
                str += ch;
            }
            return str;
        }

        // Убираем старую версию распределения запросов и делаем ниже новую машрузитацию
        public void route(HttpProcessor p)
        {

            string ContentType = "text/html";
            string Extension = "";
            //Переменная для определения запуска процедуры т.е. если клиент ввел http://localhost/index -> вызовет процедуру RouterProcedure::index 
            string RoutePath = p.http_url;
            //Убираем / в начале
            if (RoutePath.IndexOf('/') != -1)
                RoutePath = RoutePath.Substring(RoutePath.IndexOf('/') + 1);
            //если был GET запрос его тоже убираем
            if (RoutePath.IndexOf('?') != -1)
                RoutePath = RoutePath.Remove(RoutePath.IndexOf('?'));
            //проверяем на скачку файла т.е. если введено http://localhost/css/style.css, то передаем переменной Extension ".css"
            if (RoutePath.IndexOf('.') != -1)
                Extension = p.http_url.Substring(p.http_url.LastIndexOf('.'));
            //Проверяем на скачку,если Extension больше 0, даем скачать файл
            if (Extension.Length != 0)
            {
                switch (Extension)
                {
                    case ".css":
                        ContentType = "text/css";
                        break;
                    case ".js":
                        ContentType = "text/javascript";
                        break;
                    case ".jpg":
                        ContentType = "image/jpeg";
                        break;
                    case ".jpeg":
                    case ".png":
                    case ".gif":
                        ContentType = "image/" + Extension.Substring(1);
                        break;
                    default:
                        if (Extension.Length > 1)
                        {
                            ContentType = "application/" + Extension.Substring(1);
                        }
                        else
                        {
                            ContentType = "application/unknown";
                        }
                        break;
                }
                FileStream FS;

                try
                {
                    
                    FS = new FileStream(@"C:\Project\Access\d2" + p.http_url, FileMode.Open, FileAccess.Read, FileShare.Read);
                    //Отправка Заголовка.
                    string Headers = "HTTP/1.1 200 OK\nContent-Type: " + ContentType + "\nContent-Length: " + FS.Length + "\n\n";
                    byte[] HeadersBuffer = Encoding.UTF8.GetBytes(Headers);
                    p.socket.GetStream().Write(HeadersBuffer, 0, HeadersBuffer.Length);

                    // Буфер для отправки клиенту данных
                    byte[] Buffer = new byte[1024];
                    // Переменная для хранения количества байт, переданных клиенту
                    int Count;
                    while (FS.Position < FS.Length)
                    {
                        // Читаем данные из файла
                        Count = FS.Read(Buffer, 0, Buffer.Length);
                        // И передаем их клиенту
                        p.socket.GetStream().Write(Buffer, 0, Count);
                    }

                }
                catch (Exception)
                {
                    // Если случилась ошибка, посылаем клиенту ошибку 500
                    p.outputStream.Write("HTTP/1.0 404 File not found\n");
                    p.outputStream.Write("Connection: close\n\n");
                }
                
                return;
            }
            else
            {
                Sessions = new Session(p);
                //если происходит вызов http://localhost/index -> вызовет процедуру RouterProcedure::index
                RouterProcedure mc = new RouterProcedure();

                //если будет вызов http://localhost/index/login, то будет искать процедуру index, передаст в параметр а login
                string[] MasRoutePathFormat = RoutePath.Split('/');
                MethodInfo m = mc.GetType().GetMethod(MasRoutePathFormat[0]);
                //Запускаем и передаем параметр:
                //p-сервер,
                //MasRoutePathFormat-путь по которуму пришел пользователь
                //connect - mysql соединение
                try
                {
                    m.Invoke(mc, new Object[] { p, MasRoutePathFormat });
                }
                catch (Exception)
                {

                    p.redirect("http://localhost:8080/index");
                    return;
                }
                //Сохраняем данные для сессии
                Sessions.push();
                //отправляем юзеру
                p.SendToUsers(MasRoutePathFormat[0]);
                connect.close();
            }
        }

   }

    //Класс вызовов процедур (http://localhost/index -> вызовет процедуру RouterProcedure::index)
    public class RouterProcedure : HttpServer
    {
        public void registration(HttpProcessor p, string[] route)
        {
            //Здесь мы видим как применяеться проверка данных
            //Способ : p.InputPOST("Password") != "null" & p.InputPOST("Username") != "null"
            //показывает нам как он много занимет места в коде и какой не практичный
            //способ formvalidation.add(p.InputPOST("Password"), "num|required");
            //показывает нам как можно выполнить любую проверку текста просто указывая их
            //здесь мы задали чтобы данные не были пустыми и содержали целочисленный тип
            formvalidation.add("Password", "required|num");
            //здесь мы задали чтобы данные не были пустыми и содержали буквы англ и русс
            //также добавил проверку на уникальность в БД is_unique[users.login] users ->БД login -> поле где проверить, 
            //ответ false если в БД уже такое есть.
            formvalidation.add("Username", "required|alpha|is_unique[users.login]");
            //здесь мы запускаем его и узнаем можно продолжать работать или просто пустить клиента на страницу registration
            if (formvalidation.Start(p))
            {
                //старый способ проверки кода просто на существование строк
                if (p.InputPOST("Password") != "null" & p.InputPOST("Username") != "null")
                    if (connect.insert_update("INSERT INTO users (`login`, `pass`, `rules`) VALUES(@0, @1, '001');", new string[] { p.InputPOST("Username"), p.InputPOST("Password") }))
                        p.redirect("http://localhost:8080/login");
            }
        }

        public void exit(HttpProcessor p, string[] route)
        {
            Sessions.exit();
            p.redirect("http://localhost:8080/login");
        }

        public void index(HttpProcessor p, string[] route)
        {
            string str = "Добро пожаловать" + Sessions.item("login") +" Вы "; 
            //проверка сэссии пользователя и его прав.
                str += (Sessions.rules(Session.RulesType.user)) ? "Пользователь" : "Нет";
            if (Sessions.item("auth") != "null")
                p.HTML.Body.Add("welcome", str);
            else p.HTML.Body.Add("welcome", "Авторизируйтесь");
        }

        public void admin(HttpProcessor p, string[] route)
        {
            if (Sessions.rules(Session.RulesType.admin) || Sessions.rules(Session.RulesType.moderator))
            {
                string str = "Добро пожаловать" + Sessions.item("login") + " Вы ";
                //проверка сэссии пользователя и его прав.
                str += (Sessions.rules(Session.RulesType.user)) ? "Пользователь" : "Нет";
                if (Sessions.item("auth") != "null")
                    p.HTML.Body.Add("welcome", str);
                else p.HTML.Body.Add("welcome", "Авторизируйтесь");
            }
            else p.redirect("http://localhost:8080/index");
        }

        public void auth(HttpProcessor p, string[] route)
        {
           if (p.InputPOST("token") != "null")
           {
               System.Net.WebRequest req = System.Net.WebRequest.Create("https://ulogin.ru/token.php" + "?" + "token=" + p.InputPOST("token") + "&host=localhost");
               System.Net.WebResponse resp = req.GetResponse();
               System.IO.Stream stream = resp.GetResponseStream();
               System.IO.StreamReader sr = new System.IO.StreamReader(stream);
               string Out = sr.ReadToEnd();
               sr.Close();
               Out =  Out.Trim(new char [] {'{', '}'});
               string[] TempStr = Out.Split(',');
               foreach (string s in TempStr)
               {
                   string[] TempStr2 = s.Split(new string[] { Convert.ToChar(34) + ":" + Convert.ToChar(34) },StringSplitOptions.None);
                   Sessions.add(TempStr2[0].Trim('"'), System.Text.RegularExpressions.Regex.Unescape(TempStr2[1].Trim('"')));
               }

           }
        }

        public void login(HttpProcessor p, string[] route)
        {
            if (p.InputPOST("Password") != "null" & p.InputPOST("Username") != "null")
                if (connect.select("Select `id_users`, `login`, `rules` from `users` Where `login`='" + p.InputPOST("Username") + "' and `pass`='" + p.InputPOST("Password") + "';"))
                   while (connect.MyReader.Read())// Читаем
                    {
                        Sessions.add("id_users", connect.MyReader.GetValue(0).ToString());
                        Sessions.add("login", connect.MyReader.GetValue(1).ToString());
                        Sessions.add("rules", connect.MyReader.GetValue(2).ToString());
                        Sessions.add("auth", "1");
                        p.redirect("http://localhost:8080/index");
                    }
        }
    }

    public class TestMain {
        public static int Main(string[] args) {
            /*
            //--------запуск проверки--------

            Services serv = new Services();
            serv.services();
 
            //--------------------------------
            */
            //отправка смс сообщения обязательно номер начинается с + 
            //SendSMS.Send( "привет", "+79113214073", "COM5");
            HttpServer httpServer;
            if (args.GetLength(0) > 0) {
                httpServer = new HttpServer();
                httpServer.HttpServerP(Convert.ToInt16(args[0]));
            } else {
                httpServer = new HttpServer();
                httpServer.HttpServerP(8080);
            }

            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            thread.Start();
            return 0;
        }

    }

}



