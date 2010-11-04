/*
Copyright (C) 2010 Pierre St Juste <ptony82@ufl.edu>, University of Florida

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
var gquery = "";
var del = "";

$(document).ready(init);

function init() {
  document.title = "SocialDNS";
  loadPage();
  loadHeader();
  loadSearch();
  getState();
  window.setInterval(getState, 15000);
}

function loadPage() {
  $("<div/>", {'id' : 'wrapper'}).appendTo("body");
  $("<div/>", {'id' : 'header'}).appendTo("#wrapper");
  $("<div/>", {'id' : 'subheader'}).appendTo("#header");
  $("<div/>", {'id' : 'maindiv'}).appendTo("#wrapper");
  $("<div/>", {'id' : 'searchdiv'}).appendTo("#maindiv");
  $("<div/>", {'id' : 'inputdiv'}).appendTo("#maindiv");
  $("<div/>", {'id' : 'sidediv'}).appendTo("#maindiv");
  $("<div/>", {'id' : 'resultsdiv'}).appendTo("#maindiv");
}

function loadHeader() {
  $("<h1/>", {text : 'SocialDNS'}).appendTo("#subheader");
  var menu = $("<ul/>").appendTo("#subheader");
  menu.append($("<li/>", {text : 'Add Mapping', click : loadAdd}));
}

function loadSearch() {
  $("<input/>", {"name" : "search"}).appendTo("#searchdiv");

  var msg = "Search";
  $("<button/>", {text : msg, click : doSearch}).appendTo("#searchdiv");

}

function parseResult(result) {
  result.alias = $("Alias", result).text();
  result.ip = $("IP", result).text();
  result.address = $("Address", result).text();
  result.source = $("Source", result).text();
  result.rating = $("Rating", result).text();
  result.responders = new Array();
  $("string", result).each ( function() {
    result.responders.push($(this).text());})
  return result;
}

function loadResults(state) {
  $("#resultsdiv").text("");
  $("#sidediv").text("");
  createSideTable();
  createTable();

  $("Mappings > DnsMapping", state).each( function() { 
    addSideResult(parseResult(this));});

  $("TmpMappings > DnsMapping", state).each( function() { 
    addResult(parseResult(this));});
}

function createSideTable() {
  var table = $("<table/>").appendTo("#sidediv");
  var row = $("<tr/>").appendTo(table);

  var title = "Local Mappings (Click to delete)";
  var infocol = $("<td/>", { text: title, 'class' : 'table_title'});
  infocol.appendTo(row);
}

function createTable() {
  var table = $("<table/>").appendTo("#resultsdiv");
  var row = $("<tr/>").appendTo(table);

  var title = "Search Results (Click on mapping to add to your DNS cache)";
  var infocol = $("<td/>", { text: title, 'width' : '100%', 
    'class' : 'table_title'});
  var ratingcol = $("<td/>");
  infocol.appendTo(row);
  ratingcol.appendTo(row);
}

function addResult(result) {
  var row = $("<tr/>").appendTo("#resultsdiv table");
  var infocol = $("<td/>", { 'width': '100%'});
  var ratingcol = $("<td/>");
  infocol.appendTo(row);
  ratingcol.appendTo(row);

  var info = result.alias + " - " + result.ip;
  infocol.append($("<p/>", {text: info, 'class' : 'name',
    'id' : info, click : doClickAdd}));

  var sinfo = "Responders: ";
  for(var i in result.responders) {
    sinfo += result.responders[i] + ", ";
  }
  infocol.append($("<p/>", { text: sinfo, 'class' : 'info'}));

  ratingcol.append($("<span/>", {text: result.rating,'class': 'rating'}));

  $("body").data(info, result);
}

function addSideResult(result) {
  var row = $("<tr/>").appendTo("#sidediv table");
  var info = result.alias + "  -  " + result.ip;
  var infocol = $("<td/>", {text: info, 'style' : 'cursor:pointer',
  'id' : info, click : loadDelete});
  infocol.appendTo(row);

  $("body").data(info, result);
}

function clearInput() {
  $("#inputdiv").dialog("close");
  $("#inputdiv").text("");
}

function loadAdd() {
  clearInput();
  var msg;
 
  var nlabel = "Enter a domain name";
  var iplabel = "Enter IP address or leave blank to name this machine";
  $("<label/>", { text : nlabel}).appendTo("#inputdiv");
  $("<input/>", { 'type' : "text", 'class' : "input",
    'name' : "dnsname"}).appendTo("#inputdiv");
  $("<label/>", { text : iplabel}).appendTo("#inputdiv");
  $("<input/>", { "type" : "text", 'class' : "input",
    'name' : "dnsip"}).appendTo("#inputdiv");

  msg = "Create a new DNS mapping";
  $("#inputdiv").dialog({ modal : true, title : msg, width : 700,
    buttons : { "Add Mapping" : doAdd, "Cancel" : clearInput}});
}

function loadDelete() {
  clearInput();

  var result = $("body").data(this.id);
  del = result.alias;

  var msg = "Are you sure you want to delete " + result.alias + " ?";
  $("<p/>", { text : msg}).appendTo("#inputdiv");

  msg = "Delete for " + result.alias;
  $("#inputdiv").dialog({ modal : true, title : msg, width : 700,
    buttons : { "Delete" : doDelete, "Cancel" : clearInput}});
}

function getState() {
  var method = "sdns.search";
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method +
    "&q=" + gquery, success: processState});
}

function doClickAdd() {
  var result = $("body").data(this.id);
  var method = "sdns.add";
  var name = result.alias;
  var ip = result.ip;
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&n=" + name + "&i=" + ip + "&q=" + gquery, 
    success: processState});
}

function doDelete() {
  var method = "sdns.del";
  var name = del;
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&n=" + name + "&q=" + gquery, 
    success: processState});
  clearInput();
}

function doAdd() {
  var method = "sdns.add";
  var name = encodeURIComponent($(":input[name=dnsname]").val());
  var ip = encodeURIComponent($(":input[name=dnsip]").val());
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&n=" + name + "&i=" + ip + "&q=" + gquery, 
    success: processState});
  clearInput();
}

function doSearch() {
  var method = "sdns.search";
  var query = encodeURIComponent($(":input[name=search]").val());
  gquery = query;
  $.ajax({type: "POST", url: "state.xml", data : "m=" + method + 
    "&q=" + query, success: processState});
  clearInput();
}

function processState(state) {
  var exception = $("string", state).text();
  var responders = $("Responders", state).text();
  if(exception != "" && responders == "") {
    alert(exception);
    return;
  }

  loadResults(state);
}
