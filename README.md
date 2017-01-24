# Pro Miner Pipe Tunnel

Connects Named Pipes created by Windows Services to User Desktop Apps

ProMinerPipeTunnel can be run as a Windows Service or from the Desktop with Administrator Privileges

# Command Line Options

  * `-private:` Name of Pipe published by Windows Service (default:"geth.private.ipc")
  * `-published:` Name of Pipe published for Desktop Apps (default:"geth.ipc")
  * `-sid:` Windows Username for Published Pipe security(default:null [AuthenticatedUsers])

Example:

```
ProMinerPipeTunnel.exe -private:geth.private.ipc -published:geth.ipc -sid:MyWindowsUsername
```

# Config file

Options can also be specified in a configuration file called "ProMinerPipeTunnel.config" in JSON format

Example:

```json
{
	"PrivatePipeName"   : "geth.private.ipc",
	"PublishedPipeName" : "geth.ipc",
	"SecurityId"        : "MyWindowsUsername"
}
```

# Run as Windows Service

To install Pro Miner Pipe Tunnel as a Windows Service, running under the `LocalSystem` account, open a command prompt as Administrator and type:

```
ProMinerPipeTunnel.exe install
```

This will install ProMinerPipeTunnel as a Service called `"Pro Miner Pipe Tunnel"`

Add the JSON configuration file described above and use the Windows Services MMC to set the service options

You can also control the Service using:

```
ProMinerPipeTunnel.exe start
```

and

```
ProMinerPipeTunnel.exe stop
```

To uninstall the service, type:

```
ProMinerPipeTunnel.exe uninstall
```

## License

This software is licensed under the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.en.html)

