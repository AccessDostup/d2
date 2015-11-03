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

// offered to the public domain for any use with no restriction
// and also with no warranty of any kind, please enjoy. - David Jeske. 

// simple HTTP explanation
// http://www.jmarshall.com/easy/http/

namespace Bend.Util {

    public class MySQLCon
    {
        string MySQL_host = "localhost";
        string MySQL_port = "3306";
        string MySQL_uid = "root";
        string MySQL_pwd = "12345";

        public bool conn()
        {
            // Создаем соединение.
            MySqlConnection Connection = new MySqlConnection("Data Source=" + MySQL_host + ";Port=" + MySQL_port + ";User Id=" + MySQL_uid + ";Password=" + MySQL_pwd + ";");
            MySqlCommand Query = new MySqlCommand(); // С помощью этого объекта выполняются запросы к БД
            Query.Connection = Connection; // Присвоим объекту только что созданное соединение
                try
                {
                    Console.WriteLine("Соединяюсь с сервером базы данных...");
                    Connection.Open();// Соединяемся
                }
                catch (MySqlException SSDB_Exception)
                {
                    // Ошибка - выходим
                    Console.WriteLine("Проверьте настройки соединения, не могу соединиться с базой данных!\nОшибка: " + SSDB_Exception.Message);
                    return false;
                }

            Console.WriteLine("OK");
            return true;
        }
    }



    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer srv;
        private Stream inputStream;
        public StreamWriter outputStream;

        //Здесь буду храниться данные переданные браузером серверу POST & GET
        public Hashtable MasInputPost = new Hashtable();
        public Hashtable MasInputGet = new Hashtable();
        //Структура шаблонов
        public struct HTMLBody
        {
            public Hashtable Header;
            public string Head;
            public string Body;
            public string Footer;
        }

        //Переменная шаблонов
        public HTMLBody HTML;
        public String http_method;
        public String http_url;
        public String http_protocol_versionstring;
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
                //Инициализируем Header
                HTML.Header = new Hashtable();
                if (http_method.Equals("GET")) {
                    handleGETRequest();
                } else if (http_method.Equals("POST")) {
                    handlePOSTRequest();
                }
            } catch (Exception e) {
                Console.WriteLine("Exception: " + e.ToString());
                writeFailure();
            }
            outputStream.Flush();
            // bs.Flush(); // flush any remaining output
            MasInputGet = null; MasInputPost = null;
            inputStream = null; outputStream = null; // bs = null;            
            socket.Close();             
        }

        public void parseRequest() {
            String request = streamReadLine(inputStream);
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
            String line;
            while ((line = streamReadLine(inputStream)) != null) {
                if (line.Equals("")) {
                    Console.WriteLine("got headers");
                    return;
                }
                
                int separator = line.IndexOf(':');
                if (separator == -1) {
                    throw new Exception("invalid http header line: " + line);
                }
                String name = line.Substring(0, separator);
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
                         String.Format("POST Content-Length({0}) too big for this simple server",
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
            //Разделяем POST на ключ=значение
            string[] data = inputData.ReadToEnd().Split('&');
            //Переводим данные POST в ключ -> значение
            foreach (string i in data)
            {
                string[] InputPostTemp = i.Split('=');
                MasInputPost.Add(InputPostTemp[0], InputPostTemp[1]);
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
                    MasInputGet.Add(InputPostTemp[0], InputPostTemp[1]);
                }
            }
        }

        private void parsecookie(string val)
        {

        }

        //Собираем и отправляем HTML результат пользователю
        public void SendToUsers()
        {
            ReplaceMark();
            //отправка заголовков
            int len;
            string CompilHeader = "";
            //Для передачи HTML браузеру необходимо указать длину, считаем все.
            len = (HTML.Head != null) ? HTML.Head.Length : 0;
            len += (HTML.Body != null) ? HTML.Body.Length : 0;
            len += (HTML.Footer != null) ? HTML.Footer.Length : 0;
            //если длина больше и заголовок не равено 0 то добавить длину
            if (len > 0 & HTML.Header.Count != 0)
                HTML.Header.Add("Content-Length:", len);
            //Считываем ключ и значение и собираем заголовок
            foreach (DictionaryEntry s in HTML.Header.Values)
            {
                CompilHeader += s.Key + "" + s.Value + "\n";
            }
            //Отправка заголовка
               outputStream.Write(CompilHeader + "\n");

            //отправка HTML
            outputStream.Write(HTML.Head +  HTML.Body + HTML.Footer);
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

        //Процедура для автозамены маркеров
        private void ReplaceMark()
        {
            try
            {
                HTML.Head = HTML.Head.Replace("<%basepath%>", "http://localhost:8080");
            }
            catch (Exception)
            {

            }
        }

        public void redirect(string url)
        {
            HTML.Header.Add("HTTP/1.1", "301 Moved Permanenrly");
            HTML.Header.Add("Location: ", url);
            SendToUsers();
        }

        public void writeSuccess() {
            outputStream.Write("HTTP/1.0 200 OK\n");
            outputStream.Write("Content-Type: text/html\n");
            outputStream.Write("Connection: close\n");
            outputStream.Write("\n");
        }

        public void writeFailure() {
            outputStream.Write("HTTP/1.0 404 File not found\n");
            outputStream.Write("Connection: close\n");
            outputStream.Write("\n");
        }
    }

    public abstract class HttpServer {

        protected int port;
        TcpListener listener;
        bool is_active = true;
       
        public HttpServer(int port) {
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

        public abstract void route(HttpProcessor p);
   }

    public class MyHttpServer : HttpServer {
        public MyHttpServer(int port)
            : base(port) {
        }

        char Simbol(int num)
        {
            if (num < 0 || num >= 26) throw new IndexOutOfRangeException();
            return "ABCDEFGHIJKLMNOPQRSTUVWXYZ"[num];
        }

        // Убираем старую версию распределения запросов и делаем ниже новую машрузитацию
        public override void route(HttpProcessor p)
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
                    // Буфер для хранения принятых от клиента данных
                    byte[] Buffer = new byte[1024];
                    // Переменная для хранения количества байт, принятых от клиента
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
                    p.writeFailure();
                    return;
                }
            }
            else
            {
                if (p.httpHeaders["Cookie"].ToString() != "")
                    p.HTML.Header.Add("Set-Cookie", "id=" + Simbol(26));
                //если происходит вызов http://localhost/index -> вызовет процедуру RouterProcedure::index
                RouterProcedure mc = new RouterProcedure();
                //если будет вызов http://localhost/index/login, то будет искать процедуру index, передаст в параметр а login
                string[] MasRoutePathFormat = RoutePath.Split('/');
                System.Reflection.MethodInfo m = mc.GetType().GetMethod(MasRoutePathFormat[0]);
                //Запускаем и передаем параметр p
                try
                {
                    m.Invoke(mc, new Object[] { p, MasRoutePathFormat });
                } catch (Exception)
                {

                    p.redirect("http://localhost:8080/index");
                    return;
                }
                //Добавляем в HTMl голову и низ
                p.HTML.Head = System.IO.File.ReadAllText(@"C:\Project\Access\d2\template/header.html");
                p.HTML.Footer = System.IO.File.ReadAllText(@"C:\Project\Access\d2\template/footer.html");
                //отправляем юзеру
                p.SendToUsers();
            }
        }

    }
    //Класс вызовов процедур (http://localhost/index -> вызовет процедуру RouterProcedure::index)
    public class RouterProcedure
    {
        public void registration(HttpProcessor p, string[] route)
        {
 /*           MySQLCon connect = new MySQLCon(); //переменная соединения с базой
            p.HTML.Body = System.IO.File.ReadAllText(@".\template/reg.html");
            p.HTML.Body += "<b>" + route[1] + "</b>";

            p.MasInputPost.ContainsKey("");
            p.MasInputPost.ContainsValue("");*/
        }

        public void index(HttpProcessor p, string[] route)
        {
            MySQLCon connect = new MySQLCon(); //переменная соединения с базой
            p.outputStream.WriteLine(@"template/index.html");
            connect.conn();
        }
        public void login(HttpProcessor p, string[] route)
        {
            MySQLCon connect = new MySQLCon(); //переменная соединения с базой
            p.HTML.Body = System.IO.File.ReadAllText(@".\template/login.html");
            p.HTML.Body += "<b>" + route[1] +"</b>";
        }
    }


    public class TestMain {
        public static int Main(String[] args) {

            HttpServer httpServer;
            if (args.GetLength(0) > 0) {
                httpServer = new MyHttpServer(Convert.ToInt16(args[0]));
            } else {
                httpServer = new MyHttpServer(8080);
            }
            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            thread.Start();
            return 0;
        }

    }

}



