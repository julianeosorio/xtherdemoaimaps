using System;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Drawing;

using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

using AiMaps.Dal;
using AiMaps.Gsqa.Core;
using AiMaps.Gateway.SeleniumControllers;
using AiMaps.Executor;
using AiMaps.Common.Selenium;
using AiMaps.Gateway;
using Gsqa.GHeart.Metrics.Core.Models.Performance;

namespace GreenSQA.AiMaps.CustomLogic
{
	public class SmartMap
	{
	AiModel thisModel = null;

	//TODO: Razor implementation pending
	//ItemActionPerformer razorActionPerformer = null;

	string logFilePath = string.Empty;
	
	GSe se = null;

	IWebDriver driver = null;	  
	
	public AiModel ThisModel
	{
	get { return thisModel; }
	}

	public RealUserMonitoring Rum {get {return this.mapExecutor.Rum;} }

	private MapExecutor mapExecutor = null;
	
	public SmartMap(object objMapExecutor)
	{
		mapExecutor = (objMapExecutor as MapExecutor);
		thisModel = (mapExecutor.MapModel as AiModel);
		this.driver = thisModel.SeDriver;
		this.logFilePath = Path.Combine(System.IO.Path.GetDirectoryName(thisModel.ModelPath), "log.txt");
		//this.razorActionPerformer = new ItemActionPerformer();   
		this.se = new GSe(thisModel.SeDriver, "0");
	}
    
	public delegate void VoidNonParameterDelegate(); 

	public void InitRum(int rumTimeLimitMilliseconds = 120000) 
	{ 
		if (this.mapExecutor.Rum == null){
			this.mapExecutor.Rum = new RealUserMonitoring (thisModel.SeDriver, thisModel.ExecutionContext, this.mapExecutor.CorrelationString, rumTimeLimitMilliseconds); 
		} else {
			this.mapExecutor.Rum.SeDriver = thisModel.SeDriver;
		}
		
		this.mapExecutor.Rum.ProjectInfoRum.BrowserName = thisModel.ExecutionContext.WebBrowserName;
		this.mapExecutor.Rum.ProjectInfoRum.BrowserVersion = thisModel.ExecutionContext.WebBrowserVersion;
		this.mapExecutor.Rum.ProjectInfoRum.BotAuthor = thisModel.Map.RobotAuthor;

		this.mapExecutor.Rum.RunRum();
	} 

	private void SendRumPayload(string transactionName, long toleratingSeconds, string fromStep, string toStep, string errorReason = null) 
	{ 
		AiStep fStep = ThisModel.GetStep(fromStep); 
		AiStep tStep = ThisModel.GetStep(toStep);

		double start  = (fStep.StartTime.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds; 
		double end    = ((tStep.EndTime == DateTime.MinValue) || (tStep.EndTime < fStep.StartTime) ) ? DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds : tStep.EndTime.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds;   
		long duration = (long)(end-start); 

		this.mapExecutor.Rum.SendRumTransaction(transactionName, toleratingSeconds, (long)start, duration, errorReason, false);
	} 

	public void TRum(VoidNonParameterDelegate myTransaction, string transactionName, long toleratingSeconds, string fromStep, string toStep) 
	{ 
		try 
		{ 
			myTransaction(); 
			SendRumPayload(transactionName, toleratingSeconds, fromStep, toStep, null); 
		} 
		catch(Exception ex) 
		{ 
			mapExecutor.Rum.NotifyForStopping();
			Console.WriteLine("ErrorInTransactionRUM: " + transactionName + " - " + ex.Message);     
			SendRumPayload(transactionName, toleratingSeconds, fromStep, toStep, ex.Message); 
			throw ex; 
		}
	}
	
	public void TRumError(string transactionName, string fromStep, string toStep, string errorMessage) 
	{ 
		mapExecutor.Rum.NotifyForStopping();
		Console.WriteLine("ErrorInTransactionRUM: " + transactionName + " - " + errorMessage);     
		SendRumPayload(transactionName, 0, fromStep, toStep, errorMessage); 
		throw new Exception("Transaction " + transactionName + " failed"); 
	}

	public void TRumResult(long toleratingSeconds, string errorReason = null)
	{
		double start  = (DateTime.Now.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds; 
		this.mapExecutor.Rum.SendRumTransaction("TRESULT", toleratingSeconds, (long)start, this.mapExecutor.ElapsedMs, errorReason, true);
	}
	
	public bool TryRun(string stepPath)
	{
	return TryRun(stepPath, false);
	}

	public bool TryRun(string stepPath, bool ignoreCode)
	{
	bool result = true;

	try
	{
	result = Run(stepPath, ignoreCode);
	}
	catch
	{
	result = false;
	}

	return result;
	}

	public string Run(string stepPath)
	{
	Run(stepPath, false);
	return thisModel.GetStep(stepPath).Value.ToString();
	}

	public bool Run(string xPath, bool ignoreCode)
	{
	return mapExecutor.RunAction(xPath, ignoreCode);
	}
	
	public AiStep Step(string stepPath)
	{
	return thisModel.GetStep(stepPath);
	}
	
	public string StepVal(string stepPath)
	{
	return thisModel.GetStep(stepPath).Value.ToString();
	}

	public string StepValStr(string stepPath)
	{
	return thisModel.GetStep(stepPath).Value.ToString();
	}

	public Int32 StepValInt(string stepPath)
	{
	return System.Convert.ToInt32(thisModel.GetStep(stepPath).Value);
	}
	
	public Int64 StepValInt64(string stepPath)
	{
	return System.Convert.ToInt64(thisModel.GetStep(stepPath).Value);
	}

	public decimal StepValDecimal(string stepPath)
	{
	return System.Convert.ToDecimal(thisModel.GetStep(stepPath).Value);
	}
	
	public object StepValObj(string stepPath)
	{
	return thisModel.GetStep(stepPath).Value;
	}
	
	public void SetVal(string stepPath, object stepVal)
	{
		Console.WriteLine ("Se debe cambiar la propiedad val del AiStep desde String a tipo object");
		thisModel.GetStep(stepPath).Value = stepVal.ToString();
	}

	private float GetTargetThreshold(AiStep stp)
	{
	float targetThreshold = thisModel.Map.Threshold;
	if (stp.Threshold != 0) 
	{
		targetThreshold = (float)stp.Threshold / 100f;
	} 
	return targetThreshold;
	}

	public List<string> FindAll(string xPath)
	{
		/*
	string[] parts = xPath.Split(new char[] { '>' });
	AIStage stg = thisModel.GetStage(parts[0]);
	CtrlAction stp = stg.GetStep(parts[1]);
	
	razorActionPerformer = new ItemActionPerformer(stg.Type, null, null, null,
								GetTargetThreshold(stp), stp.Timeout,
								stp.OffestPoint, string.Empty, null,
								Point.Empty, stp.CustomErrorMessage, stp.MouseMotionParams);

	return thisModel.FindAll(xPath);*/
	Console.WriteLine("Computer vision find all not implemented yet");
	return null;
	}

	public bool RunTap(string elementItem)
	{
	Console.WriteLine("method not implemented yet");
	return true; //razorActionPerformer.Tap(elementItem);
	}
	
	public bool RunClick(string elementItem)
	{
		Console.WriteLine("method not implemented yet");
	//return razorActionPerformer.Click(elementItem);
	return true;
	}

	public bool RunDoubleClick(string elementItem)
	{
		Console.WriteLine("method not implemented yet");
	//return razorActionPerformer.DoubleClick(elementItem);
	return true;
	}

	public bool RunRightClick(string elementItem)
	{
		Console.WriteLine("method not implemented yet");
	//return razorActionPerformer.RightClick(elementItem);
	return true;
	}

	public bool RunMouseOver(string elementItem)
	{
		Console.WriteLine("method not implemented yet");
	//return razorActionPerformer.Hover(elementItem);
	return true;
	}

	public void  SendTextEmail(string fromEmail, string toEmail, string mailSubject, string mailBody,
							int clientPort, string clientHost, string credentialUserName, string credentialUserPassword)
	{
		Console.WriteLine("method not implemented yet");
		/*
	razorActionPerformer.SendTextEmail(fromEmail, toEmail, mailSubject, mailBody,
							clientPort, clientHost, credentialUserName, credentialUserPassword);
							*/
	}

	public bool RunMinimize(string elementItem)
	{
	Console.WriteLine("method not implemented yet");
	return true;
	//return razorActionPerformer.RunMinimizeWindow(elementItem);
	}  

	public bool RunMaximize(string elementItem)
	{
	Console.WriteLine("method not implemented yet");
	return true;
	//return razorActionPerformer.RunMaximizeWindow(elementItem);
	}
	
	public void LogFile(string textLine)
	{
	Console.WriteLine("method not implemented yet");
	
	//razorActionPerformer.WriteLineToFile(logFilePath, textLine, true);
	}

	public void LogFile(string filePath, string textLine)
	{
	Console.WriteLine("method not implemented yet");
	
	//razorActionPerformer.WriteLineToFile(filePath, textLine, true);
	}
	
	public void LogFile(string filePath, string textLine, bool append)
	{
		Console.WriteLine("method not implemented yet");
	//razorActionPerformer.WriteLineToFile(filePath, textLine, append);
	}

	public void WriteLineToFile(string textLine)
	{
		Console.WriteLine("method not implemented yet");
	//razorActionPerformer.WriteLineToFile(logFilePath, textLine, true);
	}

	public void WriteLineToFile(string filePath, string textLine, bool append)
	{
		Console.WriteLine("method not implemented yet");
	//razorActionPerformer.WriteLineToFile(filePath, textLine, append);
	}
	
	public void WriteLineToFile(string textLine, bool append)
	{
		Console.WriteLine("method not implemented yet");
	//razorActionPerformer.WriteLineToFile(logFilePath, textLine, append);
	}
	
	public void Message(string messageText, string messageTittle)
	{
		Console.WriteLine($"The message window for [{messageText}] with tittle [{messageTittle}] is not implemented yet ");
	}

	public void Message(string messageText)
	{
		Console.WriteLine($"The message window for [{messageText}] is not implemented yet ");
	}
	
	public void Message(int someNumber)
	{
		Console.WriteLine($"The message window for [{someNumber}] is not implemented yet ");
	}
	
	public void Sleep(int milliseconds)
	{
		System.Threading.Thread.Sleep(milliseconds);
	}
	
	//#LOGIC_METHOD#
}
}