<!-- SHIELDS -->
<div align="center">

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]

</div>

<!-- PROJECT LOGO -->
<br />
<div align="center">
  <h1 align="center">ServiceDiscovery</h1>

  <p align="center">
    A simple example .NET implementation of a distributed service discovery mechanism similar to Apple Bonjour.
    <br />
    <a href="https://github.com/thgossler/ServiceDiscovery/issues">Report Bug</a>
    ·
    <a href="https://github.com/thgossler/ServiceDiscovery/issues">Request Feature</a>
    ·
    <a href="https://github.com/thgossler/ServiceDiscovery#contributing">Contribute</a>
    ·
    <a href="https://github.com/sponsors/thgossler">Sponsor project</a>
    ·
    <a href="https://www.paypal.com/donate/?hosted_button_id=JVG7PFJ8DMW7J">Sponsor via PayPal</a>
  </p>
</div>


## Overview

An simple example .NET implementation of a distributed service discovery mechanism similar to Apple Bonjour. 
It should be compliant to the DNS Service Discovery (DNS-SD RFC 6763) over Multicast DNS (mDNS RFC 6762) standard.
Support for announcing and discovering multiple services in parallel is implemented. The service to service 
communication can also be secured (Https) with a server certificate. The mDNS communication is not secured
in this simple implementation.

Created with help of GitHub Copilot and ChatGPT.


## Usage

Just build the project and execute the DiscoveryClient executable 3 times somewhere in the same network, like:

Computer 1: `DiscoveryClient.exe 1`

Computer 2: `DiscoveryClient.exe 2`

Computer 3: `DiscoveryClient.exe 3`

The 3 different instances of the executable can also be started on the same computer.

They are all just starting and waiting until the 2 others have also started and been discovered, and then
they call each other in the mesh and combine their results into the output string "Hello world!" 
(instance 1 => "Hello", instance 2 => "world", instance 3 => "!"). All clients exit once they have achieved
their goal.

The `MdnsServiceDiscovery` library can handle any number of clients. Just this `DiscoveryClient` console 
example app is using exactly 3 service instances to demonstate the dynamic discovery and
collaboration based on roles.


## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".
Don't forget to give the project a star :wink: Thanks!

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request


## Donate

If you wish to use the tool but are unable to contribute, please consider donating an amount that reflects its value to you. You can do so either via PayPal

[![Donate via PayPal](https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif)](https://www.paypal.com/donate/?hosted_button_id=JVG7PFJ8DMW7J)

or via [GitHub Sponsors](https://github.com/sponsors/thgossler).


## License

Distributed under the MIT License. See [`LICENSE`](https://github.com/thgossler/ServiceDiscovery/blob/master/LICENSE) for more information.


<!-- MARKDOWN LINKS & IMAGES (https://www.markdownguide.org/basic-syntax/#reference-style-links) -->
[contributors-shield]: https://img.shields.io/github/contributors/thgossler/ServiceDiscovery.svg
[contributors-url]: https://github.com/thgossler/ServiceDiscovery/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/thgossler/ServiceDiscovery.svg
[forks-url]: https://github.com/thgossler/ServiceDiscovery/network/members
[stars-shield]: https://img.shields.io/github/stars/thgossler/ServiceDiscovery.svg
[stars-url]: https://github.com/thgossler/ServiceDiscovery/stargazers
[issues-shield]: https://img.shields.io/github/issues/thgossler/ServiceDiscovery.svg
[issues-url]: https://github.com/thgossler/ServiceDiscovery/issues
[license-shield]: https://img.shields.io/github/license/thgossler/ServiceDiscovery.svg
[license-url]: https://github.com/thgossler/ServiceDiscovery/blob/master/LICENSE
