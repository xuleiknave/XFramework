using System;

namespace Starline.Server.Host
{
    /// <summary>
    /// Settings of the server process, from command-line arguments or environment variables
    /// (an argument wins). Secrets such as the database password go in environment variables when the
    /// platform allows it, so they do not show up in the process list.
    /// </summary>
    public sealed class HostOptions
    {
        /// <summary>--port / STARLINE_PORT. UOS server images listen on 8899 in all of UOS's examples.</summary>
        public int Port = 8899;

        /// <summary>--mongo / STARLINE_MONGO: a MongoDB connection string. Takes precedence over the UOS CRUD lookup.</summary>
        public string Mongo;

        /// <summary>--db-name / STARLINE_DB_NAME.</summary>
        public string DatabaseName = "starline";

        /// <summary>--db-id / STARLINE_DB_ID and --db-password / STARLINE_DB_PASSWORD: which UOS CRUD MongoDB to use.</summary>
        public string DatabaseId = "";
        public string DatabasePassword = "";

        /// <summary>--memory / STARLINE_STORE=memory: keep everything in memory (lost on restart), for trying the server out.</summary>
        public bool Memory;

        /// <summary>--dev-login / STARLINE_DEV_LOGIN=1: accept the developer login. Only for the development UOS app.</summary>
        public bool DevLogin;

        public static HostOptions Parse(string[] args)
        {
            var o = new HostOptions
            {
                Mongo = Env("STARLINE_MONGO"),
                DatabaseId = Env("STARLINE_DB_ID") ?? "",
                DatabasePassword = Env("STARLINE_DB_PASSWORD") ?? "",
                Memory = Env("STARLINE_STORE") == "memory",
                DevLogin = Env("STARLINE_DEV_LOGIN") == "1",
            };
            if (int.TryParse(Env("STARLINE_PORT"), out int port))
            {
                o.Port = port;
            }
            o.DatabaseName = Env("STARLINE_DB_NAME") ?? o.DatabaseName;

            for (int i = 0; i < args.Length; i++)
            {
                string Next()
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException(args[i] + " 后面缺少参数");
                    }
                    return args[++i];
                }
                switch (args[i])
                {
                    case "--port":
                        o.Port = int.Parse(Next());
                        break;
                    case "--mongo":
                        o.Mongo = Next();
                        break;
                    case "--db-name":
                        o.DatabaseName = Next();
                        break;
                    case "--db-id":
                        o.DatabaseId = Next();
                        break;
                    case "--db-password":
                        o.DatabasePassword = Next();
                        break;
                    case "--memory":
                        o.Memory = true;
                        break;
                    case "--dev-login":
                        o.DevLogin = true;
                        break;
                    default:
                        throw new ArgumentException("不认识的参数 " + args[i]);
                }
            }
            return o;
        }

        private static string Env(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
