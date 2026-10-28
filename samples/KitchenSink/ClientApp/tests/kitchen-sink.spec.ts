import { test, expect } from "@playwright/test";

test("SSR markup is styled before JavaScript runs", async ({ browser, baseURL }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL });
  const page = await context.newPage();
  try {
    const response = await page.goto("/");
    expect(await response!.text()).toContain('data-server-rendered="true"');
    await expect(page.locator('head link[rel="stylesheet"]')).toHaveCount(1);
    await expect(page.locator(".sidebar")).toHaveCSS("position", "fixed");
    await expect(page.locator(".workspace")).toHaveCSS("margin-left", "248px");
    await expect(page.locator("main h1")).toHaveCSS("font-size", "38px");
  } finally {
    await context.close();
  }
});

test("all demos hydrate without browser errors and work at a mobile viewport", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  const response = await page.goto("/");
  const html = await response!.text();
  expect(html).toContain('data-server-rendered="true"');
  expect(html.match(/id="kitchen-sink"/g)).toHaveLength(1);
  await expect(
    page.getByRole("heading", { name: "Everything, in good company." }),
  ).toBeVisible();
  await page.screenshot({
    path: "test-results/overview-desktop.png",
    fullPage: true,
  });
  for (const path of [
    "/contacts",
    "/forms",
    "/props",
    "/merging",
    "/feed",
    "/polling",
    "/navigation",
    "/rendering",
    "/minimal",
  ]) {
    const response = await page.goto(path);
    expect(response!.status(), path).toBe(200);
    await expect(page.locator("main h1")).toBeVisible();
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");
  await page.screenshot({
    path: "test-results/overview-mobile.png",
    fullPage: true,
  });
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(390);
  expect(errors).toEqual([]);
});

test("contacts validate, persist across reloads, search, update and delete", async ({
  page,
}) => {
  await page.goto("/contacts");
  await page.getByRole("button", { name: "Create contact" }).click();
  await expect(page.getByText("The Name field is required.")).toBeVisible();
  const name = `Browser Contact ${Date.now()}`;
  await page.getByLabel("Name", { exact: true }).fill(name);
  await page.getByLabel("Email", { exact: true }).fill("browser@example.com");
  await page.getByRole("button", { name: "Create contact" }).click();
  await expect(page.getByTestId("flash")).toContainText("Contact created");
  await page.reload();
  await expect(
    page.getByTestId("contact").filter({ hasText: name }),
  ).toBeVisible();
  await page.getByRole("button", { name: `Edit ${name}`, exact: true }).click();
  await page.getByLabel("Name", { exact: true }).fill(`${name} Edited`);
  await page.getByRole("button", { name: "Update contact" }).click();
  await expect(page.getByTestId("flash")).toContainText("Contact updated");
  await page.getByLabel("Search contacts").fill(`${name} Edited`);
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await expect(page.getByTestId("contact")).toHaveCount(1);
  await page
    .getByRole("button", { name: `Delete ${name} Edited`, exact: true })
    .click();
  await expect(page.getByTestId("flash")).toContainText("Contact deleted");
  await page.getByLabel("Search contacts").fill(name);
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await expect(page.getByTestId("contact")).toHaveCount(0);
});

test("Precognition validates without submitting; uploads use their own error bag", async ({
  page,
}) => {
  await page.goto("/forms");
  await page.getByLabel("Profile name").fill("X");
  const validation = page.waitForResponse(
    (response) =>
      response.url().endsWith("/forms/profile") &&
      response.request().headers()["precognition"] === "true",
  );
  await page.getByLabel("Profile email").focus();
  expect((await validation).status()).toBe(422);
  await expect(page.getByTestId("profile-name-error")).not.toBeEmpty();
  await expect(page.getByTestId("flash")).toHaveCount(0);
  await page.getByRole("button", { name: "Upload file" }).click();
  await expect(page.getByText("The File field is required.")).toBeVisible();
  await page
    .getByLabel("Choose a file")
    .setInputFiles({
      name: "hello.txt",
      mimeType: "text/plain",
      buffer: Buffer.from("Hello Inertia"),
    });
  await page.getByRole("button", { name: "Upload file" }).click();
  await expect(page.getByTestId("upload-receipt")).toContainText("hello.txt");
  await expect(page.getByTestId("upload-receipt")).toContainText("13 bytes");
  await page.getByLabel("Profile name").fill("Reader");
  await page.getByLabel("Profile email").fill("reader@example.com");
  await page.getByRole("button", { name: "Save profile" }).click();
  await expect(page.getByTestId("flash")).toContainText("Profile accepted");
});

test("deferred, rescued, optional, visible and once props work together", async ({
  page,
}) => {
  await page.goto("/props");
  const initial = await page.getByTestId("initial").textContent();
  const once = await page.getByTestId("once-token").textContent();
  await expect(page.getByTestId("deferred-summary")).toContainText(
    "same request",
  );
  await expect(page.getByTestId("rescued")).toBeVisible();
  await page.getByRole("button", { name: "Retry rescued prop" }).click();
  await expect(page.getByTestId("recovered")).toContainText("Recovered");
  await page.getByRole("button", { name: "Fetch optional prop" }).click();
  await expect(page.getByTestId("optional")).toContainText("Fetched only");
  await expect(page.getByTestId("initial")).toHaveText(initial!);
  await page.getByRole("button", { name: "Reload page props" }).click();
  await expect(page.getByTestId("once-token")).toHaveText(once!);
  await page
    .getByText("Waiting to enter the viewport…")
    .scrollIntoViewIfNeeded();
  await expect(page.getByTestId("visible-prop")).toContainText(
    "entered the viewport",
  );
});

test("merge props append, deduplicate, prepend, deep merge and reset", async ({
  page,
}) => {
  await page.goto("/merging");
  const items = page.getByTestId("merged-entries").locator("li");
  await expect(items).toHaveCount(3);
  await page.getByRole("button", { name: "Append batch" }).click();
  await expect(items).toHaveCount(6);
  await expect(page.getByTestId("preferences")).toContainText("appearance");
  await expect(page.getByTestId("preferences")).toContainText("notifications");
  await page.getByRole("button", { name: "Repeat batch" }).click();
  await expect(items).toHaveCount(6);
  await page.getByRole("button", { name: "Prepend batch" }).click();
  await expect(items).toHaveCount(9);
  await expect(items.first()).toContainText("Activity 7");
  await page.getByRole("button", { name: "Reset collection" }).click();
  await expect(items).toHaveCount(3);
});

test("infinite scroll loads every page without duplicate entries", async ({
  page,
}) => {
  await page.goto("/feed");
  const items = page.getByTestId("feed-items").locator("article");
  await expect(items).toHaveCount(6);
  await items.last().scrollIntoViewIfNeeded();
  await expect(items).toHaveCount(12);
  while ((await items.count()) < 30) {
    const before = await items.count();
    await page.getByRole("button", { name: "Load more updates" }).click();
    await expect(items).toHaveCount(before + 6);
  }
  const titles = await items.locator("h2").allTextContents();
  expect(new Set(titles).size).toBe(30);
  await expect(
    page.getByText("You’re all caught up.", { exact: false }),
  ).toBeVisible();
});

test("polling updates only its prop and can pause and resume", async ({
  page,
}) => {
  await page.goto("/polling");
  const initial = await page.getByTestId("poll-initial").textContent();
  const first = await page.getByTestId("poll-id").textContent();
  await expect(page.getByTestId("poll-id")).not.toHaveText(first!);
  await page.getByRole("button", { name: "Pause updates" }).click();
  const paused = await page.getByTestId("poll-id").textContent();
  await page.waitForTimeout(2300);
  await expect(page.getByTestId("poll-id")).toHaveText(paused!);
  await expect(page.getByTestId("poll-initial")).toHaveText(initial!);
  await page.getByRole("button", { name: "Resume updates" }).click();
  await expect(page.getByTestId("poll-id")).not.toHaveText(paused!);
});

test("remembered state, shared auth, history encryption and fragments", async ({
  page,
}) => {
  await page.goto("/navigation");
  await page.getByLabel("Your remembered note").fill("Remember this idea");
  await page
    .getByRole("link", { name: "Visit contacts · prefetched on hover" })
    .click();
  await expect(page).toHaveURL(/\/contacts$/);
  await expect(
    page.getByRole("heading", { name: "Make an introduction" }),
  ).toBeVisible();
  await page.goBack();
  await expect(page.getByLabel("Your remembered note")).toHaveValue(
    "Remember this idea",
  );
  await page.getByLabel("Display name").fill("Kitchen Reader");
  await page.getByRole("button", { name: "Sign in locally" }).click();
  await expect(
    page.getByRole("heading", { name: "Hello, Kitchen Reader." }),
  ).toBeVisible();
  await expect(page.getByTestId("member-message")).toContainText(
    "Shared conditionally",
  );
  await page.getByRole("link", { name: "Enable history encryption" }).click();
  await expect(page.getByTestId("encryption")).toContainText("enabled");
  await page.getByRole("button", { name: "Inspect response" }).click();
  await expect(page.getByTestId("page-json")).toContainText(
    '"encryptHistory": true',
  );
  await page.getByRole("link", { name: "Set a URL fragment" }).click();
  await page
    .getByRole("button", { name: "Redirect & preserve fragment" })
    .click();
  await expect(page).toHaveURL(/#fragment-demo$/);
  await page.getByRole("button", { name: "Sign out & clear history" }).click();
  await expect(
    page.getByRole("heading", { name: "A guest, for now." }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Inspect response" }).click();
  await expect(page.getByTestId("page-json")).toContainText(
    '"clearHistory": true',
  );
});

test("SSR opt-out and SSR failures fall back to interactive client rendering", async ({
  page,
}) => {
  for (const query of ["csr=true", "failSsr=true"]) {
    const response = await page.goto(`/rendering?${query}`);
    const html = await response!.text();
    expect(response!.status()).toBe(200);
    expect(html).not.toContain('data-server-rendered="true"');
    expect(html.match(/id="kitchen-sink"/g)).toHaveLength(1);
    await expect(page.getByTestId("hydration")).toHaveText(
      "React is interactive in the browser.",
    );
  }
  const response = page.waitForResponse((response) =>
    response.url().endsWith("/rendering/error"),
  );
  await page.getByRole("link", { name: "Try the error page" }).click();
  expect((await response).status()).toBe(500);
  await expect(
    page.getByRole("heading", { name: "A soft place to land." }),
  ).toBeVisible();
});

test("protocol rejects missing antiforgery tokens and signals version and location changes", async ({
  request,
}) => {
  expect(
    (
      await request.post("/contacts", {
        data: { name: "Blocked", email: "blocked@example.com", team: "Design" },
      })
    ).status(),
  ).toBe(400);
  const version = await request.get("/contacts", {
    headers: { "X-Inertia": "true", "X-Inertia-Version": "outdated" },
  });
  expect(version.status()).toBe(409);
  expect(version.headers()["x-inertia-location"]).toContain("/contacts");
  const location = await request.get("/navigation/location", {
    headers: { "X-Inertia": "true", "X-Inertia-Version": "kitchen-sink-v1" },
  });
  expect(location.status()).toBe(409);
  expect(location.headers()["x-inertia-location"]).toBe(
    "/navigation?location=full-visit",
  );
  const prefetch = await request.get("/contacts", {
    headers: {
      "X-Inertia": "true",
      "X-Inertia-Version": "kitchen-sink-v1",
      Purpose: "prefetch",
      "X-Inertia-Prefetch": "10",
    },
  });
  expect(prefetch.headers()["cache-control"]).toContain("private");
});
