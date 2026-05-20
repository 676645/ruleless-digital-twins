# SmartNode FMU Project

## Overview

This project contains Functional Mock-up Units (FMUs) for the SmartNode implementation, including NordPool, Fakepool, and Yr-Weather integrations.

## Prerequisites
> **⚠️ KNOWN ISSUE (`roomM370.so` or `Femyou` missing errors):** 
> If you encounter an error stating that `roomM370.so` does not exist or that Femyou files are missing/broken, this is often due to authentication or cloning issues when downloading the project (e.g., using GitHub Desktop, CLI, or downloading as a ZIP file, which can skip or break the Femyou git submodules). The maintainers behind Femyou have been notified of this issue. Ensure the repository is fully cloned with its submodules, and then follow the OMC setup steps below to correctly compile the FMU binaries.

> **⚠️ IMPORTANT:** To compile and run this project properly, you **MUST** either use Windows Subsystem for Linux (WSL) or use macOS. Since the `.NET` application runs in a Linux environment, you **must** install the native Linux version of OpenModelica (`omc`) directly inside WSL. Even if OpenModelica is installed on Windows, it will not work because the system requires the generation of Linux `.so` binaries for the FMUs.

> Link to video of backend running "https://hvl.cloud.panopto.eu/Panopto/Pages/Viewer.aspx?id=72e66b46-803c-4d11-a2ac-b45000925af3"

### OpenModelica (OMC) Setup in WSL

Run the following commands in your WSL terminal to install OpenModelica (these commands assume an Ubuntu 24.04 "noble" distribution):

```bash
# 1. Install required dependencies
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg

# 2. Add the OpenModelica GPG key
sudo curl -fsSL http://build.openmodelica.org/apt/openmodelica.asc | sudo gpg --dearmor -o /usr/share/keyrings/openmodelica-keyring.gpg

# 3. Add the OpenModelica APT repository 
echo "deb [arch=amd64 signed-by=/usr/share/keyrings/openmodelica-keyring.gpg] https://build.openmodelica.org/apt noble release" | sudo tee /etc/apt/sources.list.d/openmodelica.list

# 4. Update package lists and install omc (OpenModelica)
sudo apt-get update
sudo apt-get install -y openmodelica
```

Once installed, build the OMC FMUs so your `.NET` code can use them by executing:

```bash
cd SmartNode/Implementations/FMUs/Source && make clean all && cp roomM370.fmu au_incubator.fmu ..
```

### OpenModelica (OMC) Setup on macOS

If you are running this project natively on a Mac, your `.NET` code will require the FMUs to be compiled with macOS native binaries (`.dylib`). OpenModelica must be installed on your Mac.

Using [Homebrew](https://brew.sh/), you can install OpenModelica:

```bash
# Tap the openmodelica repository
brew tap openmodelica/openmodelica

# Install OpenModelica
brew install openmodelica
```

Once installed, build the OMC FMUs by running the same make command in your terminal:

```bash
cd SmartNode/Implementations/FMUs/Source && make clean all && cp roomM370.fmu au_incubator.fmu ..
```

### Windows & .NET 8.0 Setup

- **Windows Subsystem for Linux (WSL)** is required
- Ensure WSL is installed and configured on your system
- .NET 8.0 in WSL

.NET 8.0 can be installed in WSL using the following commands:

```bash
sudo apt update
sudo apt install -y dotnet-sdk-8.0
```

## Building Python FMUs Manually (Windows WSL & macOS)

If the VS Code **Run Task -> Build all Python FMUs** and **(Re)Build all OMC FMUs** fail, follow the manual setup steps below.

### Step 1: NordPool FMU

Navigate to the NordPool-FMU directory and set up the Python environment.

**For Windows (WSL):**
```bash
# Example path: cd /mnt/c/Users/<windows_username>/ruleless-digital-twins/SmartNode/Implementations/FMUs/Nordpool-FMU

cd /mnt/<filepath>/<project_name>/SmartNode/Implementations/FMUs/Nordpool-FMU

sudo apt update
sudo apt install -y python3-venv python3-pip

python3 -m venv .venv
source .venv/bin/activate
```

**For macOS:**
```bash
# Mac does not use 'apt' or require 'sudo' for managing native Python environments
cd SmartNode/Implementations/FMUs/Nordpool-FMU

# Setup virtual environment natively
python3 -m venv .venv
source .venv/bin/activate
```

**For both platforms (run inside the activated virtual environment):**
```bash
python3 -m pip install --upgrade pip
python3 -m pip install pythonfmu requests requests_cache pandas
pythonfmu --version

pip install pytz
make clean all
cp NordPool.fmu ..
```
### Step 2: Fakepool FMU

```bash
cd ../Fakepool-FMU
source ../Nordpool-FMU/.venv/bin/activate

pip install -r requirements.txt
make clean all
cp Fakepool.fmu ..
```
### Step 3: Yr-Weather FMU

```bash
cd ../Yr-Weather-FMU
source ../Nordpool-FMU/.venv/bin/activate

pip install -r requirements.txt
make clean all
cp YrWeather.fmu ..
```

## Running locally on Windows
To run the SmartNode control loop coordinator locally on Windows, ensure you have .NET 8 installed and configured in WSL. Then, navigate to the `SmartNode/SmartNode` and execute the following command:

```bash 
dotnet run 
```
```
⚠️ Note that it will use `smartnode/smartnode/Properties/appsettings.json` as the configuration file, so make sure to update any changeable settings in that file as needed before running the command.

### In case of Java error in WSL you can try the following workaround:
```bash
sudo apt update
sudo apt install default-jre-headless
```

## Connect frontend to backend
After running the control loop coordinator as described above, you can connect the frontend by launching the website using `npx serve .` in the website directory [(separate GitHub project)](https://github.com/676645/iot-tree-visualization) and then opening settings and clicking on `connect`. Make sure the coordinator is running before clicking `connect`.


---
## Below is the original README content, which is still relevant and will be updated with more details soon.

## Introduction
This is the accompanying artifact to the Spajić and Stolz 2025 DataMod paper ([preprint as PDF](http://foldr.org/selabhvl/2025/2025-datamod-preprint.pdf)). It consists of an ontology, instance models, an inference engine JAR and source code, inference (and verification) rules, control loop (logic) codebase, and a simulation model (FMU) and its source code.

## Overview of the System
Coming soon!

## Requirements
- .NET 8 (for running the control loop coordinator)
- Java (for running the inference engine)
- MongoDB (for hosting the case repository - not needed if you are not using this)

## Docker-based example

The Dockerfile builds and runs the example inside the container. Note that `arm64` (and hence e.g. Apple Silicon) is currently not supported by one of the libraries that we depend on; see below for a workaround.
We use OpenModelica inside the container to compile the example FMU(s) into matching binaries.

```
% docker build -t smartnode -f SmartNode/SmartNode/Dockerfile SmartNode
...
=> => unpacking to docker.io/library/smartnode:latest
% docker run --rm -v `pwd`/models-and-rules:/app/models smartnode /app/models/inferred-model-1.ttl
info: Logic.Mapek.MapekManager[0]
      Starting the MAPE-K loop.
...
```

### MongoDB in Docker
Since MongoDB is required to use the case-based functionality, there are some setup steps required to make it run (and persist) in Docker:
1. Create a network in Docker:
```
docker network create <your_network_name>
```
2. Connect the MongoDB and coordinator containers to the newly-created network:
```
docker network connect <your_network_name> <container_name>
```

## Running the Control Loop Coordinator (SmartNode)
The codebase is a .NET 8 solution consisting of multiple projects: `Logic` (MAPE-K and models), `Implementations` (for user-provided sensor/actuator implementations), `SmartNode` (startup and configuration project), and `TestProject` (unit and integration tests). We also include our own fork of [Femyou](https://codeberg.org/SELab_HVL/vsto-Femyou) for the logic that loads and executes our FMUs. Users may choose between running the solution natively or containerized.

The codebase uses a `appsettings.json` in the `SmartNode/Properties` directory as a configuration file. This file already comes preconfigured, but users are free to change their own settings. It contains the following parameters:
1. Filepath arguments:
  - `InferenceEngineFilepath`: the filepath of the inference engine JAR file.
  - `OntologyFilepath`: the filepath of the `ruleless-digital-twins.ttl` ontology.
  - `InstanceModelFilepath`: the filepath of the ontological instance model that describes the TT components and all properties and conditions of interest.
  - `InferenceRulesFilepath`: the filepath of the inference rules used for inferring information from the instance model.
  - `InferredModelFilepath`: the filepath of the inferred model to be created upon inference.
  - `FmuDirectory`: the solution's FMU storage directory.
  - `DataDirectory`: the solution's data storage directory for persisting data values from MAPE-K cycles.
2. Coordinator settings:
  - `SimulatedEnvironment`: a string for selecting a preconfigured simulated environment implementation. Leave blank if you wish to use a real one.
  - `SaveMapekData`: a boolean for saving MAPE-K cycle data to the disk.
  - `StartInReactiveMode`: a boolean for setting the starting mode of the coordinator. Running it in reactive mode means the system will only simulate corrective actions given the respective system actuators to mitigate the current optimal condition violations. In case of no violations of optimal conditions, the system will not simulate actions. In proactive mode, the system takes a proactive approach and simulates regardless of optimal condition status, subsequently including all existing system actuators. As a result, the proactive approach checks for potential violations of optimal conditions before they happen. Conversely, the reactive approach requires less simulating and is thus more performant.
  - `UseCaseBasedFunctionality`: a boolean for using the functionality where the system uses previously-saved actions for already encountered conditions.
  - `MaximumMapekRounds`: the maximum number of MAPE-K cycles to run before termination. Setting this value to -1 runs the solution indefinitely.
  - `SimulationDurationSeconds`: sets the duration of simulations in FMU time (not real-world time).
  - `LookAheadMapekCycles`: the number of cycles to simulate the future for. Simulating further ahead can yield more optimal decisions in the long run, but more cycles generally means less prediction accuracy and more performance overhead.
  - `PropertyValueFuzziness`: to match encountered conditions with potentially preexisting solutions, a quantization technique is applied to enable matching against (virtually) infinite numbers of property values.
3. Database settings:
  - `ConnectionString`: the MongoDB server connection string.
  - `DatabaseName`: the name of the case database.
  - `CollectionName`: the name of the case collection in MongoDB.

The Logic project provides interfaces in the `Logic.DeviceInterfaces` directory for users to implement when providing their own custom connections to sensors and actuators. It also provides the `IValueHandler` interface for user-provided implementations of logic handling various operations with specific OWL types. The solution contains the `DoubleValueHandler`, `IntValueHandler`, and `TimespanValueHandler` as example implementations in the `SensorActuatorImplementations` project. These are registered in the `Factory` in the `SmartNode` project, where the user is expected to register other custom implementations as well.

The solution currently runs based on the example 1 instance model, found in `instance-model-1.ttl` in the `models-and-rules` directory. Running this through the inference engine produces `inferred-model-1.ttl` which is used throughout the control loop. You may also use `inferred-model-2.ttl`, representing cyber components.

By default, the solution runs with a fake (dummy) environment as its twinning target, but users can easily add their own implementations of real devices via the `Factory` class. 

The instance model contains two `OptimalConditions` that are satisfied in the first cycle by the dummy values provided by dummy sensor implementations. Users are welcome to add their own or change the value to see the effects throughout the loop. There are many logging statements showing various stages of execution as well as the specific SPARQL queries and their results. At the end of each control loop cycle, the solution should print its chosen combination of actions to take, demonstrating what the DT's decision for that cycle would be.

## Running the Inference Engine Manually
The `ruleless-digital-twins-inference-engine.jar` file (available with models and rules) can also be executed manually from the console with the following 4 arguments provided:
1. The filepath of the ontology.
2. The filepath of the instance model (that uses the ontology).
3. The filepath of the inference rules. In case of multiple inference rules files, this filepath should be of the main file that includes the others.
4. The output filepath of the inferred model.

### Example
```
$ java -jar ruleless-digital-twins-inference-engine.jar ../Ontology/ruleless-digital-twins.ttl instance-model-1.ttl inference-rules.rules inf-out.ttl
```

If you are using multiple `.rules` files for inferencing, then you must make sure to match the `@include` directive filepaths in the main `.rules` file with the placement of the JAR file executing it. This repository contains a `verification-rules.rules` file which is included by the main `inference-rules.rules` file. This means that the filepath listed under the `@include` directive by default forces the JAR file to be executed from the same directory. Executing the JAR file from another directory will therefore require updating the filepath in the `@include` directive of the `inference-rules.rules` file.

Users are also encouraged to add their own files through the included `user-rules.rules` file.

### Requirements for Using Cyber TTs
In our solution, cyber TTs are treated similarly to physical TTs. This means they use instance models conformant to the same ontology and use user-provided implementations to connect to soft sensors and software for reconfiguration as well for providing possible values for ConfigurableParameters.
