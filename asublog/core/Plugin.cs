namespace Asublog.Plugins
{
    using System;
    using Core;

    public abstract class Plugin
    {
        public virtual string Name { get; set; }
        public virtual string Version { get; set; }

        public IAsublog App { get; set; }
        public ILogger Log { get; set; }
        public IConfiguration Config { get; set; }

        protected Plugin(string name, string version)
        {
            Name = name;
            Version = version;
        }

        public virtual void Init()
        {
            Log.Info(string.Format("Initializing plugin {0} v{1}",
                Name, Version));
        }

        public virtual void Dispose() { }
    }

    public abstract class PostingPlugin : Plugin
    {
        public virtual int PingInterval { get { return 0; } }
        protected PostingPlugin(string name, string version) : base(name, version) { }
        public virtual void Ping() { }
    }

    public abstract class SavingPlugin : Plugin
    {
        protected SavingPlugin(string name, string version) : base(name, version) { }
        public abstract void Save(Post post);
        public abstract void Flush();
    }

    public abstract class LoggingPlugin : Plugin
    {
        protected LoggingPlugin(string name, string version) : base(name, version) { }
        public abstract void Info(string msg);
        public abstract void Error(string msg, Exception error = null);
        public abstract void Debug(string msg, object obj = null);
    }
}