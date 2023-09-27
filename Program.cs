// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Storage.Models;
using Azure.ResourceManager.Storage;
using Microsoft.Identity.Client.Extensions.Msal;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;
using System.Xml.Linq;
using Microsoft.Extensions.Azure;
using System.Reflection.PortableExecutable;

namespace ManageInternetFacingLoadBalancer
{

    public class Program
    {
        private static readonly string HttpProbe = "httpProbe";
        private static readonly string HttpsProbe = "httpsProbe";
        private static readonly string HttpLoadBalancingRule = "httpRule";
        private static readonly string HttpsLoadBalancingRule = "httpsRule";
        private static readonly string NatRule5000to22forVM1 = "nat5000to22forVM1";
        private static readonly string NatRule5001to23forVM1 = "nat5001to23forVM1";
        private static readonly string NatRule5002to22forVM2 = "nat5002to22forVM2";
        private static readonly string NatRule5003to23forVM2 = "nat5003to23forVM2";
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure Network sample for managing Internet facing load balancers -
         *
         * High-level ...
         *
         * - Create an Internet facing load balancer that receives network traffic on
         *   port 80 and 443 and sends load-balanced traffic to two virtual machines
         *
         * - Create NAT rules for SSH and TELNET access to virtual
         *   machines behind the load balancer
         *
         * - Create health probes
         *
         * Details ...
         *
         * Create an Internet facing load balancer with ...
         * - A frontend public IP address
         * - Two backend address pools which contain network interfaces for the virtual
         *   machines to receive HTTP and HTTPS network traffic from the load balancer
         * - Two load balancing rules for HTTP and HTTPS to map public ports on the load
         *   balancer to ports in the backend address pool
         * - Two probes which contain HTTP and HTTPS health probes used to check availability
         *   of virtual machines in the backend address pool
         * - Two inbound NAT rules which contain rules that map a public port on the load
         *   balancer to a port for a specific virtual machine in the backend address pool
         * - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23
         *
         * Create two network interfaces in the frontend subnet ...
         * - And associate network interfaces to backend pools and NAT rules
         *
         * Create two virtual machines in the frontend subnet ...
         * - And assign network interfaces
         *
         * Update an existing load balancer, configure TCP idle timeout
         * Create another load balancer
         * Remove an existing load balancer
         */
        public static async Task RunSample(ArmClient client)
        {
            string rgName = Utilities.CreateRandomName("NetworkSampleRG");
            string vnetName = Utilities.CreateRandomName("vnet");
            string loadBalancerName1 = Utilities.CreateRandomName("balancer1");
            string loadBalancerName2 = Utilities.CreateRandomName("balancer2");
            string publicIpName1 = "pip1-" + loadBalancerName1;
            string publicIpName2 = "pip2-" + loadBalancerName1;
            string frontendName = loadBalancerName1 + "-FE1";
            string backendPoolName1 = loadBalancerName1 + "-BAP1";
            string backendPoolName2 = loadBalancerName1 + "-BAP2";
            string networkInterfaceName1 = Utilities.CreateRandomName("nic1");
            string networkInterfaceName2 = Utilities.CreateRandomName("nic2");
            string availSetName = Utilities.CreateRandomName("av");
            string vmName1 = Utilities.CreateRandomName("lVM1");
            string vmName2 = Utilities.CreateRandomName("lVM2");


            rgName = "NetworkSampleRG1000";
            try
            {
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"deleting resource group...");
                var tempRG = await subscription.GetResourceGroups().GetAsync(rgName);
                await tempRG.Value?.DeleteAsync(WaitUntil.Completed);
            }
            catch (Exception ex) { }
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"Creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.WestUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);



                //=============================================================
                // Create a virtual network with a frontend and a backend subnets
                Utilities.Log("Creating virtual network with a frontend and a backend subnets...");
                VirtualNetworkData vnetInput = new VirtualNetworkData()
                {
                    Location = resourceGroup.Data.Location,
                    AddressPrefixes = { "172.16.0.0/16" },
                    Subnets =
                    {
                        new SubnetData() { Name = "Front-end", AddressPrefix = "172.16.1.0/24"},
                        new SubnetData() { Name = "Back-end", AddressPrefix = "172.16.3.0/24"},
                    },
                };
                var vnetLro = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetInput);
                VirtualNetworkResource vnet = vnetLro.Value;
                Utilities.Log($"Created a virtual network: {vnet.Data.Name}");

                //=============================================================
                // Create a public IP address
                Utilities.Log("Creating a public IP address...");

                PublicIPAddressData publicIPInput1 = new PublicIPAddressData()
                {
                    Location = resourceGroup.Data.Location,
                    PublicIPAllocationMethod = NetworkIPAllocationMethod.Static,
                    DnsSettings = new PublicIPAddressDnsSettings { DomainNameLabel = publicIpName1 },
                };
                _ = await resourceGroup.GetPublicIPAddresses().CreateOrUpdateAsync(WaitUntil.Completed, publicIpName1, publicIPInput1);
                var publicIPLro1 = await resourceGroup.GetPublicIPAddresses().GetAsync(publicIpName1);
                PublicIPAddressResource publicIP1 = publicIPLro1.Value;

                Utilities.Log($"Created a public IP address: {publicIP1.Data.Name}");

                //=============================================================
                // Create an Internet facing load balancer
                // Create a frontend IP address
                // Two backend address pools which contain network interfaces for the virtual
                //  machines to receive HTTP and HTTPS network traffic from the load balancer
                // Two load balancing rules for HTTP and HTTPS to map public ports on the load
                //  balancer to ports in the backend address pool
                // Two probes which contain HTTP and HTTPS health probes used to check availability
                //  of virtual machines in the backend address pool
                // Two inbound NAT rules which contain rules that map a public port on the load
                //  balancer to a port for a specific virtual machine in the backend address pool
                //  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23

                Utilities.Log("Creating a Internet facing load balancer with ...");
                Utilities.Log("- A frontend IP address");
                Utilities.Log("- Two backend address pools which contain network interfaces for the virtual\n"
                                   + "  machines to receive HTTP and HTTPS network traffic from the load balancer");
                Utilities.Log("- Two load balancing rules for HTTP and HTTPS to map public ports on the load\n"
                                   + "  balancer to ports in the backend address pool");
                Utilities.Log("- Two probes which contain HTTP and HTTPS health probes used to check availability\n"
                                   + "  of virtual machines in the backend address pool");
                Utilities.Log("- Two inbound NAT rules which contain rules that map a public port on the load\n"
                                   + "  balancer to a port for a specific virtual machine in the backend address pool\n"
                                   + "  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23");


                var frontendIPConfigurationId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName1}/frontendIPConfigurations/{frontendName}");
                var backendAddressPoolId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName1}/backendAddressPools/{backendPoolName1}");
                LoadBalancerData loadBalancerInput = new LoadBalancerData()
                {
                    Location = resourceGroup.Data.Location,
                    Sku = new LoadBalancerSku()
                    {
                        Name = LoadBalancerSkuName.Standard,
                        Tier = LoadBalancerSkuTier.Regional,
                    },
                    // Explicitly define the frontend
                    FrontendIPConfigurations =
                    {
                        new FrontendIPConfigurationData()
                        {
                            Name = frontendName,
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Static,
                            Subnet = vnet.Data.Subnets.First(item=> item.Name == "Front-end"),
                            PublicIPAddress = new PublicIPAddressData()
                            {
                                Id = publicIP1.Id,
                                IPAddress  = publicIP1.Data.IPAddress,
                                LinkedPublicIPAddress = new PublicIPAddressData()
                                {
                                    Id = publicIP1.Id,
                                    IPAddress =  publicIP1.Data.IPAddress,
                                }
                            }
                        }
                    },
                    BackendAddressPools =
                    {
                        new BackendAddressPoolData()
                        {
                            Name = backendPoolName1
                        }
                    },
                    // Add two rules that uses above backend and probe
                    LoadBalancingRules =
                    {
                        new LoadBalancingRuleData()
                        {
                            Name = HttpLoadBalancingRule,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            BackendAddressPoolId = backendAddressPoolId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 80,
                            BackendPort = 80,
                            EnableFloatingIP = false,
                            IdleTimeoutInMinutes = 15,
                            ProbeId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName1}/probes/{HttpProbe}"),
                        },
                        new LoadBalancingRuleData()
                        {
                            Name = HttpsLoadBalancingRule,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            BackendAddressPoolId = backendAddressPoolId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 443,
                            BackendPort = 443,
                            EnableFloatingIP = false,
                            IdleTimeoutInMinutes = 15,
                            ProbeId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName1}/probes/{HttpsProbe}"),
                        },
                    },
                    // Add two probes one per rule
                    Probes =
                    {
                        new ProbeData()
                        {
                            Name = HttpProbe,
                            Protocol = ProbeProtocol.Http,
                            Port = 80,
                            IntervalInSeconds = 10,
                            NumberOfProbes = 2,
                            RequestPath = "/",
                        },
                        new ProbeData()
                        {
                            Name = HttpsProbe,
                            Protocol = ProbeProtocol.Https,
                            Port = 443,
                            IntervalInSeconds = 10,
                            NumberOfProbes = 2,
                            RequestPath = "/",
                        }
                    },
                    // Add two nat pools to enable direct VM connectivity for
                    //  SSH to port 22 and TELNET to port 23
                    InboundNatRules =
                    {
                        new InboundNatRuleData()
                        {
                            Name = NatRule5000to22forVM1,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5000,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule5001to23forVM1,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5001,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule5002to22forVM2,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5002,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule5003to23forVM2,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5003,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        }
                    },
                };
                var loadBalancerLro1 = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName1, loadBalancerInput);
                LoadBalancerResource loadBalancer1 = loadBalancerLro1.Value;

                Utilities.Log($"Created a load balancer: {loadBalancer1.Data.Name}");

                //=============================================================
                // Create two network interfaces in the frontend subnet
                //  associate network interfaces to NAT rules, backend pools

                Utilities.Log("Creating two network interfaces in the frontend subnet ...");
                Utilities.Log("- And associating network interfaces to backend pools and NAT rules");

                var nicInput1 = new NetworkInterfaceData()
                {
                    Location = resourceGroup.Data.Location,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "default-config",
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData()
                            {
                                Id = vnet.Data.Subnets.First(item=>item.Name=="Front-end").Id
                            },
                                            //        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName1)
                //        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName2)
                //        .WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5000to22forVM1)
                //.WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5001to23forVM1);
                        }
                    }
                };
                var networkInterfaceLro1 = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, networkInterfaceName1, nicInput1);
                NetworkInterfaceResource networkInterface1 = networkInterfaceLro1.Value;
                Utilities.Log($"Created network interface: {networkInterface1.Data.Name}");



                var nicInput2 = new NetworkInterfaceData()
                {
                    Location = resourceGroup.Data.Location,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "default-config",
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData()
                            {
                                Id = vnet.Data.Subnets.First(item=>item.Name=="Front-end").Id
                            },
                            //LoadBalancerInboundNatRules = 
                            //{
                            //    new InboundNatRuleData()
                            //    {
                                    
                            //    }
                            //}
                            //        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName1)
                            //        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName2)
                            //        .WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5002to22forVM2)
                            //        .WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5003to23forVM2);
                        }
                    }
                };
                var networkInterfaceLro2 = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, networkInterfaceName2, nicInput2);
                NetworkInterfaceResource networkInterface2 = networkInterfaceLro2.Value;
                Utilities.Log($"Created network interface: {networkInterface2.Data.Name}");

                //=============================================================
                // Create an availability set

                Utilities.Log("Creating an availability set ...");

                AvailabilitySetData availabilitySetInput = new AvailabilitySetData(resourceGroup.Data.Location)
                {
                    PlatformFaultDomainCount = 2,
                    PlatformUpdateDomainCount = 4,
                };
                var availabilitySetLro = await resourceGroup.GetAvailabilitySets().CreateOrUpdateAsync(WaitUntil.Completed, availSetName, availabilitySetInput);
                AvailabilitySetResource availabilitySet = availabilitySetLro.Value;
                Utilities.Log($"Created first availability set: {availabilitySet.Data.Name}");

                //=============================================================
                // Create two virtual machines and assign network interfaces

                Utilities.Log("Creating two virtual machines in the frontend subnet ...");
                Utilities.Log("- And assigning network interfaces");

                // Create vm1
                Utilities.Log("Creating a new virtual machine...");
                VirtualMachineData vmInput1 = Utilities.GetDefaultVMInputData(resourceGroup, vmName1);
                vmInput1.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = networkInterface1.Id, Primary = true });
                //vmInput1.AvailabilitySetId  = availabilitySet.Id;
                var vmLro1 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName1, vmInput1);
                VirtualMachineResource vm1 = vmLro1.Value;
                Utilities.Log($"Created virtual machine: {vm1.Data.Name}");

                // Create vm2
                Utilities.Log("Creating a new virtual machine...");
                VirtualMachineData vmInput2 = Utilities.GetDefaultVMInputData(resourceGroup, vmName2);
                vmInput2.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = networkInterface2.Id, Primary = true });
                //vmInput2.AvailabilitySetId = availabilitySet.Id;
                var vmLro2 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName2, vmInput2);
                VirtualMachineResource vm2 = vmLro2.Value;
                Utilities.Log($"Created virtual machine: {vm2.Data.Name}");

                //=============================================================
                // Update a load balancer
                //  configure TCP idle timeout to 15 minutes

                Utilities.Log("Updating the load balancer ...");

                LoadBalancerData updateLoadBalancerInput = loadBalancer1.Data;
                updateLoadBalancerInput.LoadBalancingRules.First(item => item.Name == HttpLoadBalancingRule).IdleTimeoutInMinutes = 15;
                updateLoadBalancerInput.LoadBalancingRules.First(item => item.Name == HttpsLoadBalancingRule).IdleTimeoutInMinutes = 15;
                loadBalancerLro1 = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName1, loadBalancerInput);
                loadBalancer1 = loadBalancerLro1.Value;

                Utilities.Log("Update the load balancer with a TCP idle timeout to 15 minutes");

                //=============================================================
                // Create another public IP address
                Utilities.Log("Creating another public IP address...");

                PublicIPAddressData publicIPInput2 = new PublicIPAddressData()
                {
                    Location = resourceGroup.Data.Location,
                    PublicIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                    DnsSettings = new PublicIPAddressDnsSettings { DomainNameLabel = publicIpName2 }
                };
                _ = await resourceGroup.GetPublicIPAddresses().CreateOrUpdateAsync(WaitUntil.Completed, publicIpName2, publicIPInput2);
                var publicIPLro2 = await resourceGroup.GetPublicIPAddresses().GetAsync(publicIpName2);
                PublicIPAddressResource publicIP2 = publicIPLro2.Value;

                Utilities.Log($"Created another public IP address: {publicIP2.Data.Name}");

                //=============================================================
                // Create another Internet facing load balancer
                // Create a frontend IP address
                // Two backend address pools which contain network interfaces for the virtual
                //  machines to receive HTTP and HTTPS network traffic from the load balancer
                // Two load balancing rules for HTTP and HTTPS to map public ports on the load
                //  balancer to ports in the backend address pool
                // Two probes which contain HTTP and HTTPS health probes used to check availability
                //  of virtual machines in the backend address pool
                // Two inbound NAT rules which contain rules that map a public port on the load
                //  balancer to a port for a specific virtual machine in the backend address pool
                //  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23

                Utilities.Log("Creating another Internet facing load balancer with ...");
                Utilities.Log("- A frontend IP address");
                Utilities.Log("- Two backend address pools which contain network interfaces for the virtual\n"
                        + "  machines to receive HTTP and HTTPS network traffic from the load balancer");
                Utilities.Log("- Two load balancing rules for HTTP and HTTPS to map public ports on the load\n"
                        + "  balancer to ports in the backend address pool");
                Utilities.Log("- Two probes which contain HTTP and HTTPS health probes used to check availability\n"
                        + "  of virtual machines in the backend address pool");
                Utilities.Log("- Two inbound NAT rules which contain rules that map a public port on the load\n"
                        + "  balancer to a port for a specific virtual machine in the backend address pool\n"
                        + "  - this provides direct VM connectivity for SSH to port 22 and TELNET to port 23");

                frontendIPConfigurationId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName2}/frontendIPConfigurations/{frontendName}");
                var backendAddressPoolId1 = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName2}/backendAddressPools/{backendPoolName1}");
                var backendAddressPoolId2 = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName2}/backendAddressPools/{backendPoolName2}");
                loadBalancerInput = new LoadBalancerData()
                {
                    Location = resourceGroup.Data.Location,
                    Sku = new LoadBalancerSku()
                    {
                        Name = LoadBalancerSkuName.Standard,
                        Tier = LoadBalancerSkuTier.Regional,
                    },
                    // Explicitly define the frontend

                    FrontendIPConfigurations =
                    {
                        new FrontendIPConfigurationData()
                        {
                            Name = frontendName,
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Static,
                            Subnet = vnet.Data.Subnets.First(item=> item.Name == "Front-end"),
                            PublicIPAddress = new PublicIPAddressData()
                            {
                                IPAddress = publicIP2.Data.IPAddress
                            }
                        }
                    },
                    BackendAddressPools =
                    {
                        new BackendAddressPoolData()
                        {
                            Name = backendPoolName1
                        }
                    },
                    // Add two rules that uses above backend and probe
                    LoadBalancingRules =
                    {

                        new LoadBalancingRuleData()
                        {
                            Name = HttpLoadBalancingRule,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            BackendAddressPoolId = backendAddressPoolId1,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 80,
                            BackendPort = 80,
                            EnableFloatingIP = false,
                            IdleTimeoutInMinutes = 15,
                            ProbeId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName2}/probes/{HttpProbe}"),
                        },
                        new LoadBalancingRuleData()
                        {
                            Name = HttpsLoadBalancingRule,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            BackendAddressPoolId = backendAddressPoolId2,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 443,
                            BackendPort = 443,
                            EnableFloatingIP = false,
                            IdleTimeoutInMinutes = 15,
                            ProbeId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName2}/probes/{HttpsProbe}"),
                        },
                    },
                    // Add two probes one per rule
                    Probes =
                    {
                        new ProbeData()
                        {
                            Name = HttpProbe,
                            Protocol = ProbeProtocol.Http,
                            Port = 80,
                            IntervalInSeconds = 10,
                            NumberOfProbes = 2,
                            RequestPath = "/",
                        },
                        new ProbeData()
                        {
                            Name = HttpsProbe,
                            Protocol = ProbeProtocol.Https,
                            Port = 443,
                            IntervalInSeconds = 10,
                            NumberOfProbes = 2,
                            RequestPath = "/",
                        }
                    },
                    // Add two nat pools to enable direct VM connectivity for
                    //  SSH to port 22 and TELNET to port 23
                    InboundNatRules =
                    {
                        new InboundNatRuleData()
                        {
                            Name = NatRule5000to22forVM1,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5000,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule5001to23forVM1,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5001,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule5002to22forVM2,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5002,
                            BackendPort = 22,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        },
                        new InboundNatRuleData()
                        {
                            Name = NatRule5003to23forVM2,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 5003,
                            BackendPort = 23,
                            IdleTimeoutInMinutes = 15,
                            EnableFloatingIP = false,
                        }
                    },
                };
                var loadBalancerLro2 = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName2, loadBalancerInput);
                LoadBalancerResource loadBalancer2 = loadBalancerLro2.Value;

                Utilities.Log($"Created another balancer: {loadBalancer1.Data.Name}");

                //=============================================================
                // List load balancers

                Utilities.Log("Walking through the list of load balancers");

                await foreach (var loadBalancer in resourceGroup.GetLoadBalancers().GetAllAsync())
                {
                    Utilities.Log(loadBalancer.Data.Name);
                }

                //=============================================================
                // Remove a load balancer

                Utilities.Log("Deleting load balancer " + loadBalancerName2
                        + "(" + loadBalancer2.Id + ")");
                await loadBalancer2.DeleteAsync(WaitUntil.Completed);
                Utilities.Log("Deleted load balancer" + loadBalancerName2);
            }
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group...");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId.Name}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
            var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
            var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
            ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            ArmClient client = new ArmClient(credential, subscription);

            await RunSample(client);

            try
            {
                //=================================================================
                // Authenticate
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}