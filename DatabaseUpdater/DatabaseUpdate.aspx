<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="DatabaseUpdate.aspx.cs" Inherits="DatabaseUpdater.DatabaseUpdate" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>تحديث قاعدة البيانات</title>
    <style type="text/css">
        body {
            font-family: Arial;
            margin: 20px;
            direction: rtl;
        }
        .container {
            width: 500px;
            margin: 0 auto;
            padding: 20px;
            border: 1px solid #ccc;
            border-radius: 5px;
        }
        .form-group {
            margin-bottom: 15px;
        }
        .version-info {
            margin-bottom: 20px;
            padding: 10px;
            background-color: #e9ecef;
            border-radius: 4px;
        }
        .btn {
            padding: 8px 15px;
            background-color: #007bff;
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
        }
        .message {
            margin-top: 15px;
            padding: 10px;
            border-radius: 4px;
        }
        .success {
            background-color: #d4edda;
            color: #155724;
        }
        .error {
            background-color: #f8d7da;
            color: #721c24;
        }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="container">
            <h2>تحديث قاعدة البيانات</h2>
            <div class="version-info">
                <asp:Label ID="lblCurrentVersion" runat="server"></asp:Label>
            </div>
            <div class="form-group">
                <asp:Label ID="lblVersion" runat="server" Text="اختر إصدار التحديث:"></asp:Label>
                <asp:DropDownList ID="ddlScriptVersions" runat="server" Width="100%"></asp:DropDownList>
            </div>
            <div class="form-group">
                <asp:Button ID="btnUpdate" runat="server" Text="تطبيق التحديث" CssClass="btn" OnClick="btnUpdate_Click" />
            </div>
            <asp:Panel ID="pnlMessage" runat="server" Visible="false" CssClass="message">
                <asp:Literal ID="litMessage" runat="server"></asp:Literal>
            </asp:Panel>
        </div>
    </form>
</body>
</html>