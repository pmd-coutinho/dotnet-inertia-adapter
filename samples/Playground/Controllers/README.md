# Playground controllers

## Overview

MVC todo routes, request DTO validation and typed Inertia page props.

## Interface

- Source/configuration: [TodosController.cs](./TodosController.cs).
- `TodoInput` requires a title of 2–80 characters and carries a `Done` flag. `LoginInput` requires a name of at most 40 characters.
- `TodoIndexProps` carries `Client`, `Todos` (`ScrollProp` with `TodoPage` payload), `Stats` (`DeferProp` with `TodoStats` payload), `Total` and `ForceSsrFailure`.
- `Pages.ReactTodos` and `Pages.VueTodos` are `InertiaComponent<TodoIndexProps>` for `React/Todos` and `Vue/Todos`.
- `TodosController` base route `{client:regex(^react$|^vue$)}/todos`; actions expose `GET` index, `POST` store, `PUT {id:long}` update, `DELETE {id:long}` destroy and `POST session` login. Named routes are `Todos.index`, `Todos.store`, `Todos.update`, `Todos.destroy` and `Todos.login`.

## Invariants

- Index clamps page to `1..10000`, passes scroll metadata and deferred statistics in the `stats` group.
- Invalid `ModelState` redirects to same client todo page without mutating store/authentication.
- Update/delete missing IDs return 404; successful writes/login flash messages and redirect locally.

## Side Effects

- CRUD mutates singleton memory store; login calls cookie `SignInAsync`; actions render page responses, flash messages and redirects; deferred evaluation counts repository items.

## Verification

Working directory: repository root.

```sh
dotnet build samples/Playground/Playground.csproj
```

For generated route metadata and browser integration, use `samples/Playground/ClientApp` as the working directory and run `npm ci`, `npm run build`, `npm run test:manifest`, `npx playwright install chromium`, then `npm run test:browser`.
