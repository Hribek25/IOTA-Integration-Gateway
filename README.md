# IOTA Integration Gateway
![Logo](https://raw.githubusercontent.com/Hribek25/IOTA-Integration-Gateway/master/docs/logo.png)

Official project page is available at [http://iogateway.cloud/](http://iogateway.cloud/ "Project page")

## Change Log
Version 0.9.2.1 (latest stable)
* asp dotnet core packages upgraded: security fixes
Please kindly update dotnet core runtime before a deployment

Version 0.9.2.0 
* Code refactoring: Layer responsible for external API calls was added
* Layer for external API calls: added a new failover logic that repeats an external API call if fails
* Added new API call: Node/GetLatestMilestoneIndex

Version 0.9.1.0
* Currently in preview
* Cache layer was rewritten to be based on file system - much faster serialization/deserialization
* Internal collections of elements were optimized to process transactions faster

Version 0.9.0.0
* Currently in preview
* Main components and infrastructure have been developed


## Architecture Overview
![Architecture](https://raw.githubusercontent.com/Hribek25/IOTA-Integration-Gateway/master/Graphics/architecture_layers.png)
