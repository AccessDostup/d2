using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

// offered to the public domain for any use with no restriction
// and also with no warranty of any kind, please enjoy. - David Jeske. 

// simple HTTP explanation
// http://www.jmarshall.com/easy/http/

namespace Bend.Util {

    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer srv;

        private Stream inputStream;
        public StreamWriter outputStream;

        //Здесь буду храниться данные переданные браузером серверу POST & GET
        public Hashtable MasInputPost = new Hashtable();
        public Hashtable MasInputGet = new Hashtable();

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
            POSTDATA(new StreamReader(ms));
            GETDATA(this);
            Console.WriteLine("get post data end");
            srv.route(this);

        }

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
            listener = new TcpListener(port);
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
        // Убираем старую версию распределения запросов и делаем ниже новую машрузитацию
        public override void route(HttpProcessor p)
        {
            string RoutePath = p.http_url;
            if (RoutePath.IndexOf('/') != -1)
                RoutePath = RoutePath.Substring(RoutePath.IndexOf('/') + 1);
            if (RoutePath.IndexOf('?') != -1)
                RoutePath = RoutePath.Remove(RoutePath.IndexOf('?'));

            RouterProcedure mc = new RouterProcedure();
            System.Reflection.MethodInfo m = mc.GetType().GetMethod("index");
            m.Invoke(mc, null);
        }

    }
    public class RouterProcedure
    {
        public void index(HttpProcessor p)
        {
            p.outputStream.WriteLine("<html><body><h1>test server</h1>");
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



