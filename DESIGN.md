---
name: RailwayLab
description: Passenger task surfaces extending the existing warm neutral identity
colors:
  page: "#f4f2ec"
  surface: "#fffefa"
  ink: "#1d2c2d"
  muted: "#626e6d"
  border: "#dedfd8"
  accent: "#a34a32"
  accent-hover: "#823b2a"
  accent-soft: "#f5e8e2"
  positive: "#28725c"
  positive-soft: "#e6f2ec"
  danger: "#9c3e32"
  danger-soft: "#f8ebe7"
typography:
  heading:
    fontFamily: "Georgia, 'Times New Roman', serif"
    fontWeight: 500
    letterSpacing: "-0.025em"
  body:
    fontFamily: "Inter, ui-sans-serif, system-ui, sans-serif"
rounded:
  control: "0.4rem"
  panel: "0.85rem"
spacing:
  related: "0.5rem"
  control: "0.75rem"
  group: "1.25rem"
  section: "2rem"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "#ffffff"
    rounded: "{rounded.control}"
    padding: "0.75rem 1.2rem"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "0.75rem 1.2rem"
---

## Overview

The RailwayLab identity serves the passenger booking flow. The interface serves a booking task: route, seats, hold and confirmation state come first. The application remains a synthetic-data lab with simulated payment. Product boundaries are in PRODUCT.md.

## Colors

Warm neutral surfaces and dark green ink retain the incumbent identity. Rust indicates the primary action and current selection. Green indicates available seats and confirmation; muted surfaces indicate unavailable seats. Every state also has a text label or accessible name.

Dark tokens live beside light tokens in `src/Web/src/styles.css`. Dark accent buttons use dark foreground text through `--color-on-accent`; white text does not provide enough contrast on the lighter dark-theme accent. Auth uses the same palette and follows the browser's preferred color scheme.

## Typography

Retain Georgia for page titles and the system sans stack for controls and data. Headings use restrained negative tracking. Booking amounts and countdowns use tabular numerals. Display type never replaces a control label. The application does not download fonts.

## Layout

Search uses an introduction beside the form, followed by a full-width result list. Results group train identity, departure/arrival, fare and the next action. Seat selection pairs the coach map with a selection summary; checkout pairs the quote with hold actions. These columns stack on smaller viewports. Booking history is a divided list rather than a grid of repeated cards.

Shared task surfaces have a maximum width of 70rem; confirmation and booking history are narrower. Forms and seats remain usable at 320 CSS pixels. Titles use fixed rem sizes with explicit mobile breakpoints.

## Elevation & Depth

Use tonal surfaces and single borders for panels. Reserve positioned overlays for the account menu and notifications. Avoid stacking borders and ambient shadows around every content block.

## Shapes

Controls share a modest corner radius. Panels have softer corners. Seat buttons form a compact four-column map with a visible central aisle; this reflects the currently seeded coach layout rather than promising arbitrary train geometries.

## Components

Primary and secondary buttons, form controls, messages, journey steps, status badges and summary rows share global styles in `src/Web/src/styles.css`. Page-specific seat and result layouts remain scoped. Auth has a separate stylesheet because it is served by a different application.

All actions expose hover, keyboard focus and disabled states. Loading and error messages use status/alert semantics. The countdown uses a timer role without announcing every second. Search cancels superseded requests and retains the submitted journey in result links. Reduced motion disables nonessential transitions. A skip link reaches the main content.

## Do's and Don'ts

Keep unknown availability distinct from zero. Keep simulated payment visible before and after confirmation. Use human-readable seat labels and booking references without exposing internal failure URLs. Future service screens state their unavailable status without invented metrics or functional controls.
