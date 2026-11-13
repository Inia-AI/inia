import { defineComponent, h, type Component } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import type { ColumnDef } from '@tanstack/vue-table'
import type { WorkflowReportSummary } from '@/api/workflowReports/schema'

export interface MenuLink {
  titleKey: string
  to: string
  roles?: readonly string[]
  /** Shows only while the active organization has this capability. */
  capability?: string
}

export interface DrawerLink extends MenuLink {
  icon: string
  placement: 'first' | 'last'
}

export interface MenuGroup {
  titleKey: string
  pathPrefix: string
  links: MenuLink[]
}

export interface ReportsListFilter {
  queryKey: string
  labelKey: string
  allLabelKey: string
  options: { labelKey: string; value: string }[]
  toODataFilter: (value: string) => string
}

export interface ReportsListExtension {
  columns: ColumnDef<WorkflowReportSummary>[]
  filters: ReportsListFilter[]
  overlay: Component | null
}

export type UseReportsListExtension = () => ReportsListExtension

/** An uploaded file with the pages the platform counted for it. */
export interface CountedFile {
  name: string
  countedPages?: number | null
  countedAttachmentPages?: number | null
}

export interface UploadFormats {
  extensions: string[]
  panel: Component | null
  /** The pages a run reads from a file of these formats, given the form's request fields; undefined for any other file. */
  pagesRead?: (file: CountedFile, requestFields: Record<string, unknown>) => number | null | undefined
}

/**
 * A way to sign in that a module adds to the sign-in screen. It shows only while `GET /auth/providers` lists its
 * provider, first and above the other methods. Its form emits `signed-in` with the token answer.
 */
export interface SignInMethod {
  provider: string
  form: Component
  notice?: Component
  titleKey: string
  subtitleKey: string
  googleRefusalKeys?: Record<string, string>
}

export const extensionPoints = {
  topMenu: [] as MenuGroup[],
  drawerMenu: [] as DrawerLink[],
  homeAside: [] as Component[],
  reportsList: [] as (() => Promise<UseReportsListExtension>)[],
  reportActions: [] as Component[],
  reportMenuItems: [] as Component[],
  reportNotices: [] as Component[],
  userMenuItems: [] as Component[],
  overlays: [] as Component[],
  /** Pieces among the header's actions, before the theme switch. */
  headerItems: [] as Component[],
  /** Pieces on the page that says the account does not include a feature, before the way to the Dashboard; each gets the missing capability as `capability`. */
  notIncludedActions: [] as Component[],
  uploads: [] as UploadFormats[],
  signInMethods: [] as SignInMethod[],
  /** Each says whether an organization on this deployment can lack a capability; the app reads the list only then. */
  capabilitiesVary: [] as (() => boolean)[],
  /** Pieces under a report type's name in the new report flow; each gets the type as `workflow`. */
  reportTypeDetails: [] as Component[],
  /** Rows at the end of the new report flow's review step; each gets the chosen type as `workflow` and the uploaded files as `documents`, each with `countedPages` as the pages the run reads from it, may emit `holds` with a reason that keeps Submit off, or null to release it, and may emit `submit-label` with Submit's name, such as one with the price, or null for the usual one. */
  reviewDetails: [] as Component[],
}

// The reports list calls each extension in setup, so a module's code has to arrive before the list mounts.
export const withReportsListExtensions = async (view: Promise<{ default: Component }>) => {
  const [{ default: list }, useExtensions] = await Promise.all([
    view,
    Promise.all(extensionPoints.reportsList.map(load => load())),
  ])
  return defineComponent({ setup: () => () => h(list, { useExtensions }) })
}

/** Makes a loader wait for the module's screen messages and styles, so a piece never renders before its text. */
export type AfterScreens = <T>(load: () => Promise<T>) => () => Promise<T>

export interface PaidModule {
  /** The text the app shows before any module screen or piece opens, such as its menu entries. */
  messages: Record<string, unknown>
  /** Everything else the module's screens and pieces need, loaded with the first of them. */
  loadScreens?: () => Promise<{ messages: Record<string, unknown> }>
  routes?: RouteRecordRaw[]
  /** The capability every route of the module needs; without it an address of the module explains that the account does not include it. */
  capability?: string
  extend: (points: typeof extensionPoints, afterScreens: AfterScreens) => void
}
