import Authentication from './authentication'
import Workflows from './workflows'
import Files from './files'
import WorkflowExecutions from './workflowExecutions'
import WorkflowReports from './workflowReports'
import ContentRefinement from './contentRefinement'
import RefinementPresets from './refinementPresets'
import Jobs from './jobs'
import Health from './health'
import Meta from './meta'
import Counterparties from './counterparties'
import PeerCandidates from './peerCandidates'
import Diagnostics from './diagnostics'
import Users from './users'
import Password from './password'
import Profile from './profile'
import WorkflowCategories from './workflowCategories'
import CounterpartyTypes from './counterpartyTypes'

import GraphRag from './graphRag'
import TokenUsage from './tokenUsage'
import Pages from './pages'
import DocumentCategories from './documentCategories'
import ModelPricing from './modelPricing'
import ExecutedWorkflows from './executedWorkflows'
import Documents from './documents'
import Signatures from './signatures'
import SignaturePlayground from './signaturePlayground'
import AgentActivity from './agentActivity'

class API {
  Authentication: Authentication
  Workflows: Workflows
  WorkflowExecutions: WorkflowExecutions
  WorkflowReports: WorkflowReports
  Files: Files
  ContentRefinement: ContentRefinement
  RefinementPresets: RefinementPresets
  Jobs: Jobs
  Health: Health
  Meta: Meta
  Counterparties: Counterparties
  PeerCandidates: PeerCandidates
  Diagnostics: Diagnostics
  Users: Users
  Password: Password
  Profile: Profile
  WorkflowCategories: WorkflowCategories
  CounterpartyTypes: CounterpartyTypes
  GraphRag: GraphRag
  TokenUsage: TokenUsage
  Pages: Pages
  DocumentCategories: DocumentCategories
  ModelPricing: ModelPricing
  ExecutedWorkflows: ExecutedWorkflows
  Documents: Documents
  Signatures: Signatures
  SignaturePlayground: SignaturePlayground
  AgentActivity: AgentActivity

  constructor() {
    this.Authentication = new Authentication()
    this.Workflows = new Workflows()
    this.Files = new Files()
    this.WorkflowExecutions = new WorkflowExecutions()
    this.WorkflowReports = new WorkflowReports()
    this.ContentRefinement = new ContentRefinement()
    this.RefinementPresets = new RefinementPresets()
    this.Jobs = new Jobs()
    this.Health = new Health()
    this.Meta = new Meta()
    this.Counterparties = new Counterparties()
    this.PeerCandidates = new PeerCandidates()
    this.Diagnostics = new Diagnostics()
    this.Users = new Users()
    this.Password = new Password()
    this.Profile = new Profile()
    this.WorkflowCategories = new WorkflowCategories()
    this.CounterpartyTypes = new CounterpartyTypes()
    this.GraphRag = new GraphRag()
    this.TokenUsage = new TokenUsage()
    this.Pages = new Pages()
    this.DocumentCategories = new DocumentCategories()
    this.ModelPricing = new ModelPricing()
    this.ExecutedWorkflows = new ExecutedWorkflows()
    this.Documents = new Documents()
    this.Signatures = new Signatures()
    this.SignaturePlayground = new SignaturePlayground()
    this.AgentActivity = new AgentActivity()
  }
}

const instance = new API()
export default instance
