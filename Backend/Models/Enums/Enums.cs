namespace WhatsAppCampaignApi.Models.Enums;

public enum ContactType { Lead, Customer, Vendor }
public enum ContactStatus { New, Active, Inactive, InProgress, Contacted, Qualified, Closed }
public enum ContactSource { WhatsApp, Web, Import, Manual, Facebook, Saas }
public enum TemplateCategory { Marketing, Utility, Authentication }
public enum TemplateType { Text, Image, Video, Document }
public enum HeaderType { None, Text, Image, Video, Document }
public enum TemplateStatus { Approved, Rejected, Pending }
public enum CampaignStatus { Draft, Sending, Sent, Scheduled, Paused, Failed, PartiallyFailed, Cancelled }
public enum ScheduleType { Immediate, Scheduled }
public enum MessageStatus { Pending, Sent, Delivered, Read, Failed }
public enum ChatMessageDirection { Incoming, Outgoing, System }
public enum ChatMessageStatus { Pending, Sent, Delivered, Read, Failed, Received }
