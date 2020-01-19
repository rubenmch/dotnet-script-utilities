#!/usr/bin/env dotnet-script
#r "nuget: CommandLineParser, 2.7.82"
using CommandLine;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Mail;

public class Options
{
    [Option('d', "backupdirectory", Required = true, HelpText = "Directory to store the backups")]
    public string BackupDirectory { get; set; }

    [Option('i', "incrementalbackups", Required = false, Default=0, HelpText = "Number of incremental backups to create after the last full backup")]
    public int IncrementalBackupNumber { get; set; } = 0;

    [Option('c', "mysqluser", Required = false, HelpText = "MySQL User")]
    public string MySqlUser { get; set; }

    [Option('g', "mysqlpassword", Required = false, HelpText = "MySQL Password")]
    public string MySqlPassword { get; set; }

    [Option('p', "s3endpoint", Required = false, Default="s3.amazonaws.com", HelpText = "S3 endpoint")]
    public string S3Endpoint { get; set; } = "s3.amazonaws.com";

    [Option('r', "s3region", Required = false, Default="us-east-1", HelpText = "S3 region")]
    public string S3Region { get; set; } = "us-east-1";

    [Option('a', "s3accesskey", Required = true, HelpText = "S3 access key")]
    public string S3AccessKey { get; set; }

    [Option('s', "s3secretkey", Required = true, HelpText = "S3 secret access key")]
    public string S3SecretKey { get; set; }

    [Option('b', "s3bucket", Required = true, HelpText = "S3 bucket name")]
    public string S3Bucket { get; set; }

    [Option('f', "s3folder", Required = false, HelpText = "S3 folder name")]
    public string S3Folder { get; set; }

    [Option('u', "s3paralleluploads", Required = false, Default = 8, HelpText = "S3 number of parallel uploads")]
    public int S3ParallelUploads { get; set; } = 8;

    [Option('e', "smtpuser", Required = false, HelpText = "SMTP user")]
    public string SmtpUser { get; set; }

    [Option('e', "smtppassword", Required = false, HelpText = "SMTP password")]
    public string SmtpPassword { get; set; }

    [Option('e', "smtphost", Required = false, HelpText = "SMTP host")]
    public string SmtpHost { get; set; }

    [Option('e', "smtpport", Required = false, HelpText = "SMTP port")]
    public int SmtpPort { get; set; }

    [Option('e', "smtpfrom", Required = false, HelpText = "SMTP from")]
    public string SmtpFrom { get; set; }

    [Option('e', "smtpto", Required = false, HelpText = "SMTP to")]
    public string SmtpTo { get; set; }

    [Option('n', "notifysuccess", Required = false, Default=false, HelpText = "Set output to verbose messages")]
    public bool NotifySuccess { get; set; } = false;

    // [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
    // public bool Verbose { get; set; }
}

Parser.Default.ParseArguments<Options>(Args).WithParsed<Options>(o =>
{
    try{
    //Create backup directory if it doesn't exist
    if(!Directory.Exists(o.BackupDirectory)){
        Directory.CreateDirectory(o.BackupDirectory);
    }

    //Check if a full backup exists
    var fullBackupPath = Path.Combine(o.BackupDirectory, "full");

    //Check if incremental backup path exists
    var incrementalBackupPath = Path.Combine(o.BackupDirectory, "inc");
    if(!Directory.Exists(incrementalBackupPath)){
        Directory.CreateDirectory(incrementalBackupPath);
    }

    //Get incremental backups
    var incrementalBackupDirInfo = new DirectoryInfo(incrementalBackupPath);
    var incrementalBackups = incrementalBackupDirInfo.EnumerateDirectories(incrementalBackupPath).OrderBy(d => d.Name);
    var incrementalBackupCount = incrementalBackups.Count();

    //Check if the full backup needs to be cleaned
    if(Directory.Exists(fullBackupPath) && (o.IncrementalBackupNumber == 0 || incrementalBackupCount >= o.IncrementalBackupNumber)){
        Directory.Delete(fullBackupPath, true);
        incrementalBackupCount = 0;
    }

    var mysqlUser = !String.IsNullOrEmpty(o.MySqlUser) ? $"--user={o.MySqlUser}" : "";
    var mysqlPassword = !String.IsNullOrEmpty(o.MySqlPassword) ? $"--password={o.MySqlPassword}" : "";

    if(!Directory.Exists(fullBackupPath)){
        //Create full backup
        Directory.CreateDirectory(fullBackupPath);
        var news3Name = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}full";
        var s3folder = !String.IsNullOrEmpty(o.S3Folder) ? $"{o.S3Folder}/{news3Name}" : news3Name;
        Bash($"xtrabackup {mysqlUser} {mysqlPassword} --backup --stream=xbstream --extra-lsndir={fullBackupPath} --target-dir={fullBackupPath} | xbcloud put --storage=s3 --s3-endpoint='{o.S3Endpoint}' --s3-access-key='{o.S3AccessKey}' --s3-secret-key='{o.S3SecretKey}' --s3-bucket='{o.S3Bucket}' --s3-region='{o.S3Region}' --parallel={o.S3ParallelUploads} {s3folder}", o);

        //Notify
        if(o.NotifySuccess){
            SendEmail($"Backup {o.S3Bucket}/{s3folder} created", "Great news everyone! The backup {o.S3Bucket}/{s3folder} was successfully created", o);
        }
    }
    else{
        if(o.IncrementalBackupNumber > 0){
            //Create next incremental backup
            var nextBackupNumber = ++incrementalBackupCount;
            var nextIncrementalBackupPath = Path.Combine(incrementalBackupPath, $"inc{nextBackupNumber}");
            var baseDir = incrementalBackupCount > 0 ? incrementalBackups.ElementAt(incrementalBackupCount - 1).FullName : fullBackupPath;
            Directory.CreateDirectory(nextIncrementalBackupPath);
            var news3Name = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}inc{nextBackupNumber}";
            var s3folder = !String.IsNullOrEmpty(o.S3Folder) ? $"{o.S3Folder}/{news3Name}" : news3Name;
            Bash($"xtrabackup {mysqlUser} {mysqlPassword} --backup --stream=xbstream --incremental-basedir={baseDir} --target-dir={nextIncrementalBackupPath} | xbcloud put --storage=s3 --s3-endpoint='{o.S3Endpoint}' --s3-access-key='{o.S3AccessKey}' --s3-secret-key='{o.S3SecretKey}' --s3-bucket='{o.S3Bucket}' --s3-region='{o.S3Region}' --parallel={o.S3ParallelUploads} {s3folder}", o);
            
            //Notify
            if(o.NotifySuccess){
                SendEmail($"Backup {o.S3Bucket}/{s3folder} created", "Great news everyone! The backup {o.S3Bucket}/{s3folder} was successfully created", o);
            }
        }
    }
    }
    catch(Exception exc){
        SendEmail($"Backup {o.S3Bucket}/{o.S3Folder} failed",$"Backup {o.S3Bucket}/{o.S3Folder} creation failed with exception: {exc.ToString()}", o);
        throw;
    }
});

public void Bash(string cmd, Options o)
{
    Console.WriteLine($"> {cmd}");
    var escapedArgs = cmd.Replace("\"", "\\\"");
    bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    var process = new Process()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = isLinux ? "/bin/bash" : "cmd.exe",
            Arguments = isLinux ? $"-c \"{escapedArgs}\"" : $"/C \"{escapedArgs}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        }
    };

    process.Start();
    process.WaitForExit();

    // Read standard output
    string result = process.StandardOutput.ReadToEnd();
    if(!String.IsNullOrEmpty(result)){
        Console.WriteLine(result);
    }

    // Check exit code
    if(process.ExitCode != 0){
        Console.WriteLine($"ERROR: {cmd} exited with code {process.ExitCode}");
        //Read standard error
        string error = process.StandardError.ReadToEnd();
        if(!String.IsNullOrEmpty(error)){
            Console.WriteLine(error);
        }
        //Notify
        SendEmail($"Backup {o.S3Bucket}/{o.S3Folder} failed",$"Backup {o.S3Bucket}/{o.S3Folder} creation failed with error: {error}", o);
        Environment.Exit(1);
    }
}

public void SendEmail(string subject, string message, Options o){    
    if(!String.IsNullOrEmpty(o.SmtpHost)){
        // Credentials
        var credentials = new NetworkCredential(o.SmtpUser, o.SmtpPassword);
        // Mail message
        var mail = new MailMessage()
        {
            From = new MailAddress(o.SmtpFrom),
            Subject = subject,
            Body = message
        };
        mail.IsBodyHtml = false;
        mail.To.Add(new MailAddress(o.SmtpTo));
        // Smtp client
        var client = new SmtpClient()
        {
            Port = o.SmtpPort,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Host = o.SmtpHost,
            EnableSsl = true,
            Credentials = credentials
        };
        client.Send(mail);
    }
}