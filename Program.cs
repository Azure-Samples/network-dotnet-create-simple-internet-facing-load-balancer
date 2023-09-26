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
            string loadBalancerName1 = Utilities.CreateRandomName("intlb1");
            string loadBalancerName2 = Utilities.CreateRandomName("intlb2");
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

            try
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

                var network = azure.Networks.Define(vnetName)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithAddressSpace("172.16.0.0/16")
                        .DefineSubnet("Front-end")
                            .WithAddressPrefix("172.16.1.0/24")
                            .Attach()
                        .DefineSubnet("Back-end")
                            .WithAddressPrefix("172.16.3.0/24")
                            .Attach()
                        .Create();

                Utilities.Log("Created a virtual network");
                // Print the virtual network details
                Utilities.PrintVirtualNetwork(network);

                //=============================================================
                // Create a public IP address
                Utilities.Log("Creating a public IP address...");

                var publicIpAddress = azure.PublicIPAddresses.Define(publicIpName1)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithLeafDomainLabel(publicIpName1)
                        .Create();

                Utilities.Log("Created a public IP address");
                // Print the virtual network details
                Utilities.PrintIPAddress(publicIpAddress);

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

                var loadBalancer1 = azure.LoadBalancers.Define(loadBalancerName1)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        // Add two rules that uses above backend and probe
                        .DefineLoadBalancingRule(HttpLoadBalancingRule)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(80)
                            .ToBackend(backendPoolName1)
                            .WithProbe(HttpProbe)
                            .Attach()
                        .DefineLoadBalancingRule(HttpsLoadBalancingRule)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(443)
                            .ToBackend(backendPoolName2)
                            .WithProbe(HttpsProbe)
                            .Attach()
                        
                        // Add two nat pools to enable direct VM connectivity for
                        //  SSH to port 22 and TELNET to port 23
                        .DefineInboundNatRule(NatRule5000to22forVM1)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5000)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule5001to23forVM1)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5001)
                            .ToBackendPort(23)
                            .Attach()
                        .DefineInboundNatRule(NatRule5002to22forVM2)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5002)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule5003to23forVM2)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5003)
                            .ToBackendPort(23)
                            .Attach()

                        // Explicitly define the frontend
                        .DefinePublicFrontend(frontendName)
                            .WithExistingPublicIPAddress(publicIpAddress)
                            .Attach()

                        // Add two probes one per rule
                        .DefineHttpProbe(HttpProbe)
                            .WithRequestPath("/")
                            .WithPort(80)
                            .Attach()

                        .DefineHttpProbe(HttpsProbe)
                            .WithRequestPath("/")
                            .WithPort(443)
                            .Attach()

                        .Create();

                // Print load balancer details
                Utilities.Log("Created a load balancer");
                Utilities.PrintLoadBalancer(loadBalancer1);

                //=============================================================
                // Create two network interfaces in the frontend subnet
                //  associate network interfaces to NAT rules, backend pools

                Utilities.Log("Creating two network interfaces in the frontend subnet ...");
                Utilities.Log("- And associating network interfaces to backend pools and NAT rules");

                var networkInterfaceCreatables = new List<ICreatable<INetworkInterface>>();

                ICreatable<INetworkInterface> networkInterface1Creatable;
                ICreatable<INetworkInterface> networkInterface2Creatable;

                networkInterface1Creatable = azure.NetworkInterfaces.Define(networkInterfaceName1)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithExistingPrimaryNetwork(network)
                        .WithSubnet("Front-end")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName1)
                        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName2)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5000to22forVM1)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5001to23forVM1);

                networkInterfaceCreatables.Add(networkInterface1Creatable);

                networkInterface2Creatable = azure.NetworkInterfaces.Define(networkInterfaceName2)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithExistingPrimaryNetwork(network)
                        .WithSubnet("Front-end")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName1)
                        .WithExistingLoadBalancerBackend(loadBalancer1, backendPoolName2)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5002to22forVM2)
                        .WithExistingLoadBalancerInboundNatRule(loadBalancer1, NatRule5003to23forVM2);

                networkInterfaceCreatables.Add(networkInterface2Creatable);

                var networkInterfaces1 = azure.NetworkInterfaces.Create(networkInterfaceCreatables.ToArray());

                // Print network interface details
                Utilities.Log("Created two network interfaces");
                Utilities.Log("Network Interface ONE -");
                Utilities.PrintNetworkInterface(networkInterfaces1.ElementAt(0));
                Utilities.Log();
                Utilities.Log("Network Interface TWO -");
                Utilities.PrintNetworkInterface(networkInterfaces1.ElementAt(1));

                //=============================================================
                // Create an availability set

                Utilities.Log("Creating an availability set ...");

                var availSet1 = azure.AvailabilitySets.Define(availSetName)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithFaultDomainCount(2)
                        .WithUpdateDomainCount(4)
                        .Create();

                Utilities.Log("Created first availability set: " + availSet1.Id);
                Utilities.PrintAvailabilitySet(availSet1);

                //=============================================================
                // Create two virtual machines and assign network interfaces

                Utilities.Log("Creating two virtual machines in the frontend subnet ...");
                Utilities.Log("- And assigning network interfaces");

                var virtualMachineCreatables1 = new List<ICreatable<IVirtualMachine>>();
                ICreatable<IVirtualMachine> virtualMachine1Creatable;
                ICreatable<IVirtualMachine> virtualMachine2Creatable;

                virtualMachine1Creatable = azure.VirtualMachines.Define(vmName1)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithExistingPrimaryNetworkInterface(networkInterfaces1.ElementAt(0))
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(UserName)
                        .WithSsh(SshKey)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .WithExistingAvailabilitySet(availSet1);

                virtualMachineCreatables1.Add(virtualMachine1Creatable);

                virtualMachine2Creatable = azure.VirtualMachines.Define(vmName2)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithExistingPrimaryNetworkInterface(networkInterfaces1.ElementAt(1))
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(UserName)
                        .WithSsh(SshKey)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .WithExistingAvailabilitySet(availSet1);

                virtualMachineCreatables1.Add(virtualMachine2Creatable);

                var t1 = DateTime.UtcNow;
                var virtualMachines = azure.VirtualMachines.Create(virtualMachineCreatables1.ToArray());

                var t2 = DateTime.UtcNow;
                Utilities.Log($"Created 2 Linux VMs: (took {(t2 - t1).TotalSeconds} seconds) ");
                Utilities.Log();

                // Print virtual machine details
                Utilities.Log("Virtual Machine ONE -");
                Utilities.PrintVirtualMachine(virtualMachines.ElementAt(0));
                Utilities.Log();
                Utilities.Log("Virtual Machine TWO - ");
                Utilities.PrintVirtualMachine(virtualMachines.ElementAt(1));

                //=============================================================
                // Update a load balancer
                //  configure TCP idle timeout to 15 minutes

                Utilities.Log("Updating the load balancer ...");

                loadBalancer1.Update()
                        .UpdateLoadBalancingRule(HttpLoadBalancingRule)
                            .WithIdleTimeoutInMinutes(15)
                            .Parent()
                        .UpdateLoadBalancingRule(HttpsLoadBalancingRule)
                            .WithIdleTimeoutInMinutes(15)
                            .Parent()
                        .Apply();

                Utilities.Log("Update the load balancer with a TCP idle timeout to 15 minutes");

                //=============================================================
                // Create another public IP address
                Utilities.Log("Creating another public IP address...");

                var publicIpAddress2 = azure.PublicIPAddresses.Define(publicIpName2)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithLeafDomainLabel(publicIpName2)
                        .Create();

                Utilities.Log("Created another public IP address");
                // Print the virtual network details
                Utilities.PrintIPAddress(publicIpAddress2);

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

                var loadBalancer2 = azure.LoadBalancers.Define(loadBalancerName2)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        // Add two rules that uses above backend and probe
                        .DefineLoadBalancingRule(HttpLoadBalancingRule)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(80)
                            .ToBackend(backendPoolName1)
                            .WithProbe(HttpProbe)
                            .Attach()
                        .DefineLoadBalancingRule(HttpsLoadBalancingRule)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(443)
                            .ToBackend(backendPoolName2)
                            .WithProbe(HttpsProbe)
                            .Attach()
                        // Add two nat pools to enable direct VM connectivity for
                        //  SSH to port 22 and TELNET to port 23
                        .DefineInboundNatRule(NatRule5000to22forVM1)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5000)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule5001to23forVM1)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5001)
                            .ToBackendPort(23)
                            .Attach()
                        .DefineInboundNatRule(NatRule5002to22forVM2)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5002)
                            .ToBackendPort(22)
                            .Attach()
                        .DefineInboundNatRule(NatRule5003to23forVM2)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend(frontendName)
                            .FromFrontendPort(5003)
                            .ToBackendPort(23)
                            .Attach()
                        // Explicitly define the frontend
                        .DefinePublicFrontend(frontendName)
                            .WithExistingPublicIPAddress(publicIpAddress2)
                            .Attach()
                        // Add two probes one per rule
                        .DefineHttpProbe(HttpProbe)
                            .WithRequestPath("/")
                            .WithPort(80)
                            .Attach()
                        .DefineHttpProbe(HttpsProbe)
                            .WithRequestPath("/")
                            .WithPort(443)
                            .Attach()
                        .Create();

                // Print load balancer details
                Utilities.Log("Created another load balancer");
                Utilities.PrintLoadBalancer(loadBalancer2);

                //=============================================================
                // List load balancers

                var loadBalancers = azure.LoadBalancers.List();

                Utilities.Log("Walking through the list of load balancers");

                foreach (var loadBalancer in loadBalancers)
                {
                    Utilities.PrintLoadBalancer(loadBalancer);
                }

                //=============================================================
                // Remove a load balancer

                Utilities.Log("Deleting load balancer " + loadBalancerName2
                        + "(" + loadBalancer2.Id + ")");
                azure.LoadBalancers.DeleteById(loadBalancer2.Id);
                Utilities.Log("Deleted load balancer" + loadBalancerName2);
            }
            finally
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