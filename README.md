# Awesome.ConfigurationManagementDatabase

This project contains the core of Awesome CMDB a solution for storing all your infrastrure in a MSSQL database.

## Getting Started

These instructions will get the Project running your local PC

### Prerequisites

You will need visual studio installed and the latest version of .net SDK


### Installing

A step by step of how to run the sample application locally

* Download this repository
* Duplicate the appsettings.json and call it appsettings.Development.json in the awesome.configurationmanagementdatabase.ConsoleApp folder
* Fill in the values of an AWS account using the access key and secret
* Fill in the database section with the MySQL connection details
```json
	
{
  "connectionKeySettings": {
    "accessKeyId": "NOT SET", // setting env var Awesome_connectionKeys__accessKeyId will override this
    "secretKey": "NOT SET" // setting env var Awesome_connectionKeys__secretKey will override this
  },
  "database": {
    "databaseType": "NOT SET", // setting env var Awesome_database__databaseType will override this
    "databaseHost": "NOT SET", // setting env var Awesome_database__databaseHost will override this
    "databaseUsername": "NOT SET", // setting env var Awesome_database__databaseUsername will override this
    "databasePassword": "NOT SET", // setting env var Awesome_database__databasePassword will override this
    "databasePort": "NOT SET", // setting env var Awesome_database__databasePort will override this
    "databaseName": "NOT SET", // setting env var Awesome_database__databaseName will override this
    "whatsMyIpWebUrl": "NOT SET" // setting env var Awesome_database__whatsMyIpWebUrl will override this
  }
}

```


```Note: Don`t forget to allow access from your IP to the Database ```





### Running the example project

* Select the Console app and select it as the startup application
* Start the application
* You should now have the new servers in your Database



### And coding style tests

Coding guidlines, are default Resharper recomendations.


## Built With


* [Visual Studio](https://www.microsoft.com/visualstudio/) - VS2019

## Contributing

Please follow normal Git flow branching and pull request strategy

## Versioning

no tagging strategy defined

## Authors

* **Mark Richardson** - *Initial work* - [Tridion Ted](https://twitter.com/tridionted)


## License

This project is open source, feel free to modify or share for free or for proffit.

## Acknowledgments

* Stack exchange