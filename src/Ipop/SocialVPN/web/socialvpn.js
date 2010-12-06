/*
Copyright (C) 2009 Pierre St Juste <ptony82@ufl.edu>, University of Florida

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

var prevState = "";
var refresh = 1;
var uid = "";
var statusMsg = ""

$(document).ready(init);

function init() {
  document.title = "SocialVPN";
  loadPage();
  loadHeader();
  loadNav();
  getState();
  window.setInterval(getState, 15000);
}

function loadPage() {
  $("<div/>", {'id' : 'wrapper'}).appendTo("body");
  $("<div/>", {'id' : 'header'}).appendTo("#wrapper");
  $("<div/>", {'id' : 'subheader'}).appendTo("#header");
  $("<div/>", {'id' : 'maindiv'}).appendTo("#wrapper");
  $("<div/>", {'id' : 'userdiv'}).appendTo("#maindiv");
  $("<div/>", {'id' : 'inputdiv'}).appendTo("#maindiv");
  $("<div/>", {'id' : 'navdiv'}).appendTo("#maindiv");
  $("<div/>", {'id' : 'friendsdiv'}).appendTo("#maindiv");
}

function loadHeader() {
  $("<h1/>", {text : 'SocialVPN'}).appendTo("#subheader");
  var menu = $("<ul/>").appendTo("#subheader");
  menu.append($("<li/>", {text : 'Add', click : loadAdd}));
  menu.append($("<li/>", {text : 'Login', 'id' : "login", 
    click : loadLogin}));
  menu.append($("<li/>", {text : 'Shutdown', 'id' : "shutdown", 
    click : doShutdown}));
}

function loadNav() {
  var menu = $("<ul/>");
  menu.appendTo("#navdiv");
  var item = $("<li/>");
  item.append($("<input/>", { "type" : "checkbox", id : "Online",
    "checked" : "true", click : getState}));
  item.append($("<label/>", {text : "Online Friends"}));
  menu.append(item);
  var item = $("<li/>");
  item.append($("<input/>", { "type" : "checkbox", id : "Offline",
    "checked" : "true", click : getState}));
  item.append($("<label/>", {text : "Offline Friends"}));
  menu.append(item);
  var item = $("<li/>");
  item.append($("<input/>", { "type" : "checkbox", id : "Blocked",
    "checked" : "true", click : getState}));
  item.append($("<label/>", {text : "Blocked Friends"}));
  menu.append(item);
  var item = $("<li/>");
  item.append($("<input/>", { "type" : "checkbox", id : "Pending",
    "checked" : "false", click : getState}));
  item.append($("<label/>", {text : "Pending Friends"}));
  menu.append(item);
}

function clearInput() {
  $("#inputdiv").dialog("close");
  $("#inputdiv").text("");
}

function loadStatus() {
  clearInput();
  var user = parseUser($("LocalUser", prevState));
  msg = "P2P Address : " + user.address;
  $("<p/>", { text: msg}).appendTo("#inputdiv");
  msg = "Current User Status";
  $("#inputdiv").dialog({ modal : true, title : msg, width : 700,
    buttons : { "Close" : clearInput}});
}

function loadLogin() {
  var stat = $("Message", prevState).text();
  if(stat.match("Online") != null) {
    doLogout();
    return;
  }

  clearInput();
  var msg;

  $("<label/>", { text : "Username"}).appendTo("#inputdiv");
  $("<input/>", { 'type' : "text", 'class' : "input",
    'name' : "user"}).appendTo("#inputdiv");
  $("<label/>", { text : "Password"}).appendTo("#inputdiv");
  $("<input/>", { "type" : "password", 'class' : "input",
    'name' : "pass"}).appendTo("#inputdiv");

  msg = "Login to automatically connect to XMPP friends using SocialVPN";
  $("#inputdiv").dialog({ modal : true, title : msg, width : 700,
    buttons : { "Login" : doLogin, "Cancel" : clearInput}});
}

function loadSetUid() {
  clearInput();
  var msg;

  $("<input/>", { 'type' : "text", 'class' : "input",
    'name' : "uid"}).appendTo("#inputdiv");

  msg = "Enter your XMPP ID (ex. user@gmail.com)";
  $("#inputdiv").dialog({ modal : true, title : msg, width : 700,
    buttons : { "Submit" : doSetUid, "Cancel" : clearInput}});
}

function loadAdd() {
  clearInput();
  var user = parseUser($("LocalUser", prevState));
  var msg;

  msg = "You can test SocialVPN by connecting to our test server, \
         click on Add Test Server button below";
  $("<p/>", { text: msg, "class" : "inbold"}).appendTo("#inputdiv");

  msg = "or connect to a friend by entering his/her P2P address:";
  $("<p/>", { text: msg}).appendTo("#inputdiv");
  $("<input/>", { "class" : "input", 
    "name" : "address"}).appendTo("#inputdiv");

  msg = "or email this message to your friends:";
  $("<p/>", { text: msg, "class" : "inbold"}).appendTo("#inputdiv");

  msg = "I would like to add you to my SocialVPN network, please paste the \
         link below in your browser, make sure SocialVPN is running first";
  $("<p/>", { text: msg}).appendTo("#inputdiv");

  msg = "http://127.0.0.1:58888/state.xml?m=add&a=" + user.address +
  "&f=" + user.fpr + "&html=1";
  $("<p/>", { text: msg}).appendTo("#inputdiv");

  msg = "- Thank you";
  $("<p/>", { text: msg}).appendTo("#inputdiv");

  msg = "Add friends manually without logging into XMPP";

  $("#inputdiv").dialog({ modal : true, title : msg, width : 700,
    buttons : { "Add Friend" : doAdd, "Add Test Server" : doAddTest,
    "Cancel" : clearInput}});
}

function getState() {
  if(refresh == 0) {
    return;
  }

  $.ajax({type: "GET", url: "state.xml", success: processState});
}

function doAddTest() {
  var method = "login";
  var network = "test";
  var user = uid;
  var pass = "nopub";
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&n=" + network + "&u=" + user + "&p=" + pass, 
    success: processState});
  refresh = 1;
  clearInput();
  setMessage("looking up test servers");
}

function doLogin() {
  var method = "login";
  var network = "jabber";
  var user = encodeURIComponent($(":input[name=user]").val());
  var pass = encodeURIComponent($(":input[name=pass]").val());
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&n=" + network + "&u=" + user + "&p=" + pass, 
    success: processState});
  refresh = 1
  clearInput();
}

function doSetUid() {
  var method = "setuid";
  var uid = encodeURIComponent($(":input[name=uid]").val());
  var pcid = "";
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&u=" + uid + "&p=" + pcid, success: processState});
  refresh = 1;
  clearInput();
}

function doLogout() {
  var method = "logout";
  var network = "jabber";
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&n=" + network, success: processState});
}

function doShutdown() {
  var method = "shutdown";
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method,
    success: processState});
  $('body').html("<h3>SocialVPN is now shutdown, you can restart by clicking \
  on Start SocialVPN icon on your desktop<h3>");
}

function doAdd() {
  doCall("add",$(":input[name=address]").val());
  clearInput();
}

function doBlock() {
  doCall("block",this.id);
}

function doUnblock() {
  clearInput();
  var user = $("body").data(this.id);
  var list = $("<ul/>").appendTo("#inputdiv");

  var uid = "Uid : " + user.uid;
  $("<li/>", { text : uid }).appendTo(list);

  var pcid = "PCID : " + user.pcid;
  $("<li/>", { text : pcid }).appendTo(list);

  var address = "P2P Address : " + user.address;
  $("<li/>", { text : address }).appendTo(list);

  var fpr = "Fingerprint : " + user.fpr;
  $("<li/>", { text : fpr }).appendTo(list);

  var msg = "Confirm your friend's information (for security purposes)";
  $("#inputdiv").dialog({ modal : true, title : msg, width : 700, 
    buttons : { "Allow" : function() { doCall("unblock", user.address); 
    clearInput(); }, "Cancel" : clearInput }});
}

function doDelete() {
  doCall("del",this.id);
}

function doCall(method, address) {
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&a=" + address, success: processState});
}

function processState(state) {
  if(refresh == 0) {
    return;
  }

  var exception = $("string", state).text();
  var pending = $("Pending", state).text();
  if(exception != "" && pending == "") {
    if(exception == "Uid not set") {
      refresh = 0;
      loadSetUid();
    }
    else {
      alert(exception);
    }
    return;
  }
  prevState = state;
  processUser(state);
  loadFriends(state);
}

function parseUser(user) {
  user = $(user);
  user.uid = $("Uid", user).text();
  user.pcid = $("PCID", user).text();
  user.img = $("Pic", user).text();
  user.ip = $("IP", user).text();
  user.address = $("Address", user).text().substring(12);
  user.fpr = $("Fingerprint", user).text();
  user.alias = $("Alias", user).text();
  user.status = $("Status", user).text();
  uid = user.uid;
  return user;
}

function parsePending(user) {
  user = $(user);
  user.address = $(user).text().substring(12);
  user.status = "Pending";
  user.img = "http://www.gravatar.com/avatar/?d=mm";
  return user;
}

function processUser(state) {
  $("#userdiv").text("");
  $("<div/>", {'id' : 'userpicdiv'}).appendTo("#userdiv");
  var user = parseUser($("LocalUser", state));

  var txt = "http://gravatar.com";
  var link = $("<a/>", {'href' : txt, 'target' : '_blank'}).appendTo("#userpicdiv");

  $("<img/>", {'src' : user.img, 'width' : '60px', 
    'height' : '60px'}).appendTo(link);

  $("<div/>", {'id' : 'usersubdiv'}).appendTo("#userdiv");

  var info = " (" + user.pcid + " - " + user.ip + ")";
  var h2 = $("<h2/>", {text : user.uid}).appendTo("#usersubdiv");
  $("<span/>", {text : info}).appendTo(h2);

  var info = "P2P Address: " + user.address;
  $("<span/>", {text : info, 'class' : 'address'}).appendTo("#usersubdiv");

  $("<br/>").appendTo("#usersubdiv");

  statusMsg = $("NetworkState > Message", prevState).text();
  var msg = "Status: " + statusMsg;
  $("<p/>", {text : msg, id: 'statusMsg'}).appendTo("#usersubdiv");

  if(statusMsg.match("Online") != null) {
    $("#login").text("Logout");
  }
  else {
    $("#login").text("Login");
  }
}

function setMessage(msg) {
  $("#statusMsg").text("Status: ..." + msg + "...");
}

function loadFriends(state) {
  $("#friendsdiv").text("");
  $("<table/>").appendTo("#friendsdiv");
  $("SocialUser",state).each(function() { addOnline(parseUser(this)); });
  $("SocialUser",state).each(function() { addOffline(parseUser(this)); });
  $("SocialUser",state).each(function() { addBlocked(parseUser(this)); });
  $("Pending > string",state).each(function() {
    addFriend(parsePending(this)); });
}

function addOnline(user) {
  if(user.status == "Online") {
    addFriend(user);
  }
}

function addOffline(user) {
  if(user.status == "Offline") {
    addFriend(user);
  }
}

function addBlocked(user) {
  if(user.status == "Blocked") {
    addFriend(user);
  }
}

function checkFlags(user) {
  var id = "#" + user.status;
  return $(id).attr("checked");
}

function addFriend(user) {
  if(!checkFlags(user)) return;

  var row = $("<tr/>").appendTo("#friendsdiv table");

  var imgcol = $("<td/>");
  imgcol.appendTo(row);

  var txt = "http://gravatar.com";
  var link = $("<a/>", {'href' : txt, 'target' : '_blank'});

  $("<img/>",{'src' : user.img, 'width' : '30px', 
    'height' : '30px'}).appendTo(link);

  imgcol.append(link);

  var infocol = $("<td/>", {'width' : '100%'});
  infocol.appendTo(row);

  var info = user.uid + " (" + user.pcid + " - " + user.ip + ")";
  var address = "P2P Address : " + user.address + " : " + user.status;

  var name = "name";
  if (user.status == "Offline") {
    name = "name_b";
  }
  else if (user.status == "Blocked") {
    name = "name_c";
  }
  else if (user.status == "Pending") {
    name = "name_b";
  }

  infocol.append($("<p/>",{text: info, 'class': name}));
  infocol.append($("<p/>",{text: address, 'class': 'info'}));

  var optcol = $("<td/>");
  optcol.appendTo(row);

  if (user.status != "Blocked") {
    optcol.append($("<p/>",{text: 'Block', 'class': 'opts', 
      'id' : user.address, click : doBlock}));
  }
  else {
    optcol.append($("<p/>",{text: 'Allow', 'class': 'opts',
      'id' : user.address, click : doUnblock}));
  }

  //optcol.append($("<p/>",{text: 'Delete', 'class': 'opts',
  //  'id' : user.address, click : doDelete}));

  $("body").data(user.address, user);
}
