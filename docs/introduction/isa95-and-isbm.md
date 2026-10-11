# ISA-95 and ISBM

ISA-95, ISBM, RapidRedPanda, and Azure Service Bus describe different parts of an integration. Understanding their roles helps separate the meaning of exchanged information from the services that deliver it.

## ISA-95

ISA-95 is a family of standards for enterprise-control system integration. It provides terminology and models for information and activities shared between business and manufacturing systems. Its scope includes information structures, manufacturing operations activities, transactions, and messaging services. See the [official ISA-95 overview](https://www.isa.org/standards-and-publications/isa-standards/isa-95-standard).

ISA-95 does not identify one universal transport protocol for every implementation. An integration selects concrete interfaces and representations appropriate to the relevant models and requirements.

## Part 6 and OpenO&M ISBM 2.0

ISA-95 Part 6 is the **Messaging Service Model**. It addresses the services used to exchange information between applications. OpenO&M identifies [ISBM as an implementation specification for that model](https://www.openoandm.org/isbm/).

The relationship is between a service model and concrete implementation interfaces. ISBM 2.0 adds interface definitions for REST and SOAP; it is not another name for the whole ISA-95 family.

## Where RapidRedPanda Fits

| Layer | Role |
| --- | --- |
| ISA-95 | Models and terminology for enterprise and manufacturing integration. |
| ISA-95 Part 6 | Messaging Service Model within that standards family. |
| OpenO&M ISBM 2.0 | Concrete specification for implementing the messaging services. |
| RapidRedPanda ISBM Host | Server implementation exposing ISBM REST operations. |
| SQL Server and Azure Service Bus | Persistence and broker infrastructure used by this Host. |

Applications use RapidRedPanda's REST interfaces for messaging. The Host manages channels, sessions, and message lifecycle state and coordinates delivery using its backend services. Implementing those interfaces does not make RapidRedPanda a general implementation of ISA-95's information and activity models.

OpenO&M is the source of the ISBM specification. An application can use an ISBM server independently of a wider OpenO&M or Open Industrial Interoperability Ecosystem project.

## Information Models and Messaging Interfaces

An information model describes what data represents: for example, an equipment item, a material quantity, or a production result. A message format gives that information a representation applications can exchange. A messaging interface describes how applications submit, retrieve, and manage messages.

For example, two applications may agree on an equipment-status payload containing an identifier, state, and timestamp. ISBM supplies the messaging operations. The applications remain responsible for agreeing on the identifier namespace, allowed states, and timestamp interpretation.

### B2MML and CCOM

[B2MML, maintained by MESA International](https://github.com/MESAInternational/B2MML-BatchML), provides XML schemas implementing ISA-95 data models. [MIMOSA CCOM](https://www.mimosa.org/mimosa-ccom/) provides an information model and XML representation for asset lifecycle information.

They concern message content and its meaning. ISBM concerns messaging services. An integration can choose a suitable content model independently of its choice to use an ISBM interface.

RapidRedPanda is not a CCOM-specific server. These introductory documents do not claim that the Host validates business payloads against CCOM or B2MML schemas, transforms one model into another, or resolves differences in application semantics.

## Broker Choice

[Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview) is the message broker used by the current Host. ISBM clients communicate with the Host rather than directly with Azure Service Bus.

Other ISBM implementations can use different infrastructure while exposing the specified application-facing services. RapidRedPanda's current backend remains SQL Server and Azure Service Bus; alternative backend support has not been established.

## Further Reading

Use the linked official sources for authoritative terminology and interface definitions. These introductions are an explanation of the relationships and current Host scope, rather than a replacement for the standards or a conformance assessment.

Next: [Core Concepts](core-concepts.md). Previous: [What Is ISBM?](what-is-isbm.md). Project: [RapidRedPanda ISBM Host](../../README.md).
