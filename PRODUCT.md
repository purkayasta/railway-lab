# RailwayLab product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

The primary experience serves passengers searching for a train, selecting seats, confirming a simulated booking and reviewing their bookings. The README explains the engineering lab for developers and reviewers. This audience split was confirmed by the user.

## Product Purpose

Demonstrate a connected railway booking journey backed by independent Auth, Schedule and Reservation services. Success means a passenger can understand the next action and distinguish a selected seat, a temporary hold and a confirmed booking.

## Capabilities and Constraints

Angular is the existing frontend. Preserve the OpenIddict browser redirect flow, PKCE, session storage, permission guards and server-calculated pricing. Checkout simulates payment and charges no money. Payment, Analytics and Helpdesk services are planned. T6 passed; authentication remains incomplete until T7 live refresh/global logout checks pass. Use actual API data and distinguish unknown availability from zero seats.

## Operating Context

Public search and seat browsing precede authenticated holds and checkout. The project runs locally or through Docker Compose. Synthetic data and simulated payment must remain clearly identified.

## Brand Commitments

RailwayLab uses an ivory/rust palette, serif headings and dark green neutrals.
